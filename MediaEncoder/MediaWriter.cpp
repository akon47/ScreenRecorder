#include "pch.h"
#include "MediaWriter.h"

namespace MediaEncoder
{
	ref struct WriterPrivateData
	{
	public:
		AVFormatContext* FormatContext;
		AVCodecContext* VideoCodecContext;
		AVCodecContext* AudioCodecContext;
		const AVCodec* VideoCodec;
		const AVCodec* AudioCodec;
		AVStream* VideoStream;
		AVStream* AudioStream;

		AVFrame* VideoFrame;
		AVFrame* AudioFrame;
		AVFrame* SoftwareVideoFrame;

		int64_t NextVideoPts;
		int64_t NextAudioPts;
		int AudioSamplesCount;

		struct SwsContext* SwsContext;
		struct SwrContext* SwrContext;

		int SwsSrcWidth, SwsSrcHeight;
		AVPixelFormat SwsSrcFormat;

		AVBufferRef* HardwareDeviceContext;

		WriterPrivateData()
		{
			FormatContext = nullptr;

			VideoCodecContext = nullptr;
			AudioCodecContext = nullptr;
			VideoCodec = nullptr;
			AudioCodec = nullptr;
			VideoStream = nullptr;
			AudioStream = nullptr;

			VideoFrame = nullptr;
			AudioFrame = nullptr;

			SoftwareVideoFrame = nullptr;

			SwsContext = nullptr;
			SwrContext = nullptr;

			NextVideoPts = 0;
			NextAudioPts = 0;
			AudioSamplesCount = 0;

			SwsSrcWidth = 0;
			SwsSrcHeight = 0;
			SwsSrcFormat = AV_PIX_FMT_NONE;

			HardwareDeviceContext = nullptr;
		}
	};

	static int write_frame(AVFormatContext* fmt_ctx, AVCodecContext* c, AVStream* st, AVFrame* frame)
	{
		int ret;
		ret = avcodec_send_frame(c, frame);
		if (ret < 0)
		{
			throw gcnew IOException("avcodec_send_frame");
		}

		while (ret >= 0)
		{
			AVPacket pkt = {nullptr};

			ret = avcodec_receive_packet(c, &pkt);
			if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF)
				break;
			if (ret < 0)
			{
				throw gcnew IOException("avcodec_receive_packet");
			}

			if (pkt.duration == 0 && frame == nullptr)
				pkt.duration = 1;

			av_packet_rescale_ts(&pkt, c->time_base, st->time_base);
			pkt.stream_index = st->index;

			ret = av_interleaved_write_frame(fmt_ctx, &pkt);

			av_packet_unref(&pkt);
			if (ret < 0)
			{
				throw gcnew IOException("av_interleaved_write_frame");
			}
		}

		return ret == AVERROR_EOF ? 1 : 0;
	}

	static int set_hwframe_ctx(AVCodecContext* ctx, AVBufferRef* hw_device_ctx, int width, int height,
	                           AVPixelFormat hw_format)
	{
		AVBufferRef* hw_frames_ref;
		AVHWFramesContext* frames_ctx = nullptr;
		int err = 0;
		if (!(hw_frames_ref = av_hwframe_ctx_alloc(hw_device_ctx)))
		{
			fprintf(stderr, "Failed av_hwframe_ctx_alloc.\n");
			return -1;
		}
		frames_ctx = (AVHWFramesContext*)(hw_frames_ref->data);
		frames_ctx->format = hw_format;
		frames_ctx->sw_format = AV_PIX_FMT_NV12;
		frames_ctx->width = width;
		frames_ctx->height = height;
		if (hw_format == AV_PIX_FMT_QSV)
		{
			frames_ctx->initial_pool_size = 32;
		}

		if ((err = av_hwframe_ctx_init(hw_frames_ref)) < 0)
		{
			av_buffer_unref(&hw_frames_ref);
			return err;
		}
		ctx->hw_frames_ctx = av_buffer_ref(hw_frames_ref);
		if (!ctx->hw_frames_ctx)
			err = AVERROR(ENOMEM);
		av_buffer_unref(&hw_frames_ref);
		return err;
	}

	MediaWriter::MediaWriter(
		int width, int height, int video_numerator, int video_denominator,
		VideoCodec video_codec, int video_bitrate,
		AudioCodec audio_codec, int audio_bitrate)
		: m_width(width), m_height(height), m_videoNumerator(video_numerator), m_videoDenominator(video_denominator),
		  m_videoBitrate(video_bitrate), m_videoCodec(static_cast<AVCodecID>(video_codec)),
		  m_audioBitrate(audio_bitrate), m_audioCodec(static_cast<AVCodecID>(audio_codec)), m_data(nullptr),
		  m_disposed(false)
	{
		avformat_network_init();
	}

	void MediaWriter::Open(String^ url, String^ format, bool forceSoftwareEncoder)
	{
		CheckIfDisposed();

		Close();

		if (url == nullptr)
			throw gcnew NullReferenceException("url");

		m_data = gcnew WriterPrivateData();
		m_videoFramesCount = 0;
		m_audioSamplesCount = 0;

		IntPtr urlStringPointer = Marshal::StringToHGlobalUni(url);
		auto nativeUrlUnicode = static_cast<wchar_t*>(urlStringPointer.ToPointer());
		int utf8UrlStringSize = WideCharToMultiByte(CP_UTF8, 0, nativeUrlUnicode, -1, nullptr, 0, nullptr, nullptr);
		auto nativeUrl = new char[utf8UrlStringSize];
		WideCharToMultiByte(CP_UTF8, 0, nativeUrlUnicode, -1, nativeUrl, utf8UrlStringSize, nullptr, nullptr);

		char* nativeFormat = nullptr;

		m_url = gcnew String(url);
		if (format != nullptr)
		{
			m_format = gcnew String(format);

			IntPtr formatStringPointer = Marshal::StringToHGlobalUni(format);
			auto nativeFormatUnicode = static_cast<wchar_t*>(formatStringPointer.ToPointer());
			int utf8FormatStringSize = WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, nullptr, 0, nullptr,
			                                               nullptr);
			nativeFormat = new char[utf8FormatStringSize];
			WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, nativeFormat, utf8FormatStringSize, nullptr,
			                    nullptr);
		}
		else
			m_format = nullptr;

		AVFormatContext* formatContext = avformat_alloc_context();
		if (avformat_alloc_output_context2(&formatContext, nullptr, nativeFormat, nativeUrl) == 0)
		{
			m_data->FormatContext = formatContext;
		}

		if (m_data->FormatContext == nullptr)
		{
			throw gcnew IOException("Cannot open the file");
		}

		// Create Video Codec
		if (m_videoCodec != AV_CODEC_ID_NONE)
		{
		VIDEO_CODEC_INITIAL:
			AVCodecContext* videoCodecContext;
			AVCodecID videoCodecId = m_videoCodec == AV_CODEC_ID_PROBE && formatContext->oformat != nullptr
				                         ? formatContext->oformat->video_codec
				                         : m_videoCodec;
			const AVCodec* videoCodec = nullptr;
			if (!forceSoftwareEncoder && m_width >= 100 && m_height >= 100)
			{
				if (videoCodecId == AV_CODEC_ID_H264)
				{
					if (h264_nvenc)
					{
						videoCodec = avcodec_find_encoder_by_name("h264_nvenc");
					}
					else if (h264_qsv)
					{
						videoCodec = avcodec_find_encoder_by_name("h264_qsv");
					}
				}
				else if (videoCodecId == AVCodecID::AV_CODEC_ID_H265)
				{
					if (hevc_nvenc)
					{
						videoCodec = avcodec_find_encoder_by_name("hevc_nvenc");
					}
					else if (hevc_qsv)
					{
						videoCodec = avcodec_find_encoder_by_name("hevc_qsv");
					}
				}
			}

			if (!videoCodec)
				videoCodec = avcodec_find_encoder(videoCodecId);

			videoCodecContext = avcodec_alloc_context3(videoCodec);
			videoCodecContext->width = m_width;
			videoCodecContext->height = m_height;
			videoCodecContext->sample_aspect_ratio = av_make_q(1, 1);
			if (videoCodec->pix_fmts)
			{
				videoCodecContext->pix_fmt = videoCodec->pix_fmts[0];

				AVPixelFormat targetPixelFormat = AV_PIX_FMT_YUV420P;
				if ((videoCodecId == AV_CODEC_ID_H264 || videoCodecId == AVCodecID::AV_CODEC_ID_H265) && strncmp(
					videoCodec->name, "libx", 4) != 0)
				{
					if (h264_nvenc || hevc_nvenc)
					{
						targetPixelFormat = AV_PIX_FMT_D3D11;
					}
					else if (h264_qsv || hevc_qsv)
					{
						//targetPixelFormat = AV_PIX_FMT_QSV;
						targetPixelFormat = AV_PIX_FMT_NV12;
					}
				}
				for (int i = 0; videoCodec->pix_fmts[i] != AV_PIX_FMT_NONE; i++)
				{
					if (videoCodec->pix_fmts[i] == targetPixelFormat)
					{
						videoCodecContext->pix_fmt = videoCodec->pix_fmts[i];
					}
				}
			}
			else
				videoCodecContext->pix_fmt = AV_PIX_FMT_YUV420P;

			if (videoCodecContext->pix_fmt == AV_PIX_FMT_D3D11)
			{
				AVBufferRef* hw_device_ctx = nullptr;
				if (av_hwdevice_ctx_create(&hw_device_ctx, AV_HWDEVICE_TYPE_D3D11VA, nullptr, nullptr, 0) < 0)
				{
					throw gcnew IOException("av_hwdevice_ctx_create error");
				}
				if (set_hwframe_ctx(videoCodecContext, hw_device_ctx, m_width, m_height, AV_PIX_FMT_D3D11) < 0)
				{
					throw gcnew IOException("set_hwframe_ctx error");
				}
				m_data->HardwareDeviceContext = hw_device_ctx;
			}
			else if (videoCodecContext->pix_fmt == AV_PIX_FMT_QSV)
			{
				AVBufferRef* hw_device_ctx = nullptr;
				if (av_hwdevice_ctx_create(&hw_device_ctx, AV_HWDEVICE_TYPE_QSV, nullptr, nullptr, 0) < 0)
				{
					throw gcnew IOException("av_hwdevice_ctx_create error");
				}
				if (set_hwframe_ctx(videoCodecContext, hw_device_ctx, m_width, m_height, AV_PIX_FMT_QSV) < 0)
				{
					throw gcnew IOException("set_hwframe_ctx error");
				}
				m_data->HardwareDeviceContext = hw_device_ctx;
			}

			videoCodecContext->time_base = av_make_q(m_videoDenominator, m_videoNumerator);
			videoCodecContext->framerate = av_make_q(m_videoNumerator, m_videoDenominator);

			// reduce maximum gop size to 1 second for smoother handling in video editors and players
			videoCodecContext->gop_size = min(videoCodecContext->gop_size, av_q2d(videoCodecContext->framerate));

			videoCodecContext->bit_rate = m_videoBitrate > 0 ? m_videoBitrate : 10000000;
			// reduce bitrate tolerance to 50% of average
			videoCodecContext->bit_rate_tolerance = min(videoCodecContext->bit_rate_tolerance, videoCodecContext->bit_rate / 2);

			if (videoCodec->id == AVCodecID::AV_CODEC_ID_H264 || videoCodec->id == AVCodecID::AV_CODEC_ID_H265)
			{
				if (strncmp(videoCodec->name, "libx", 4) == 0)
				{
					av_opt_set(videoCodecContext->priv_data, "preset", "ultrafast", 0);
				}
			}

			if (avcodec_open2(videoCodecContext, videoCodec, nullptr) < 0)
			{
				if (!forceSoftwareEncoder)
				{
					forceSoftwareEncoder = true;
					avcodec_close(videoCodecContext);
					goto VIDEO_CODEC_INITIAL;
				}
				throw gcnew IOException("Cannot open video codec.");
			}

			AVStream* videoStream = avformat_new_stream(m_data->FormatContext, videoCodec);
			if ((m_data->FormatContext->oformat->flags & AVFMT_GLOBALHEADER) != 0)
				videoCodecContext->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;


			avcodec_parameters_from_context(videoStream->codecpar, videoCodecContext);
			videoStream->time_base = videoCodecContext->time_base;
			videoStream->avg_frame_rate = av_make_q(m_videoNumerator, m_videoDenominator);

			m_data->VideoCodecContext = videoCodecContext;
			m_data->VideoCodec = videoCodec;
			m_data->VideoStream = videoStream;

			m_videoCodecName = gcnew String(m_data->VideoCodecContext->codec->name);
		}

		int targetSamplerate = 48000;
		if (url->Contains(gcnew String("rtmp://")))
		{
			targetSamplerate = 44100; // for youtube recommended
		}

		// Create Audio Codec
		if (m_audioCodec != AV_CODEC_ID_NONE)
		{
			AVCodecContext* audioCodecContext;
			const AVCodec* audioCodec = avcodec_find_encoder(
				m_audioCodec == AV_CODEC_ID_PROBE && formatContext->oformat != nullptr
					? formatContext->oformat->audio_codec
					: m_audioCodec);
			AVStream* audioStream = avformat_new_stream(m_data->FormatContext, audioCodec);
			audioCodecContext = avcodec_alloc_context3(audioCodec);
			audioCodecContext->sample_fmt = audioCodec->sample_fmts ? audioCodec->sample_fmts[0] : AV_SAMPLE_FMT_FLTP;
			audioCodecContext->bit_rate = m_audioBitrate > 0 ? m_audioBitrate : 128000;
			audioCodecContext->sample_rate = 48000;
			if (audioCodec->supported_samplerates)
			{
				audioCodecContext->sample_rate = audioCodec->supported_samplerates[0];
				for (int i = 0; audioCodec->supported_samplerates[i]; i++)
				{
					if (audioCodec->supported_samplerates[i] == targetSamplerate)
						audioCodecContext->sample_rate = audioCodec->supported_samplerates[i];
				}
			}
			audioCodecContext->channel_layout = AV_CH_LAYOUT_STEREO;
			if (audioCodec->channel_layouts)
			{
				audioCodecContext->channel_layout = audioCodec->channel_layouts[0];
				for (int i = 0; audioCodec->channel_layouts[i]; i++)
				{
					if (audioCodec->channel_layouts[i] == AV_CH_LAYOUT_STEREO)
						audioCodecContext->channel_layout = AV_CH_LAYOUT_STEREO;
				}
			}
			audioCodecContext->channels = av_get_channel_layout_nb_channels(audioCodecContext->channel_layout);
			audioCodecContext->strict_std_compliance = FF_COMPLIANCE_EXPERIMENTAL;

			if ((m_data->FormatContext->oformat->flags & AVFMT_GLOBALHEADER) != 0)
				audioCodecContext->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;

			if (avcodec_open2(audioCodecContext, audioCodec, nullptr) < 0)
				throw gcnew IOException("Cannot open audio codec.");
			avcodec_parameters_from_context(audioStream->codecpar, audioCodecContext);
			audioStream->time_base = av_make_q(1, audioCodecContext->sample_rate);

			m_data->AudioCodecContext = audioCodecContext;
			m_data->AudioCodec = audioCodec;
			m_data->AudioStream = audioStream;

			m_audioCodecName = gcnew String(m_data->AudioCodecContext->codec->name);
		}
		//

		if (!(m_data->FormatContext->oformat->flags & AVFMT_NOFILE))
		{
			if (avio_open(&m_data->FormatContext->pb, nativeUrl, AVIO_FLAG_WRITE) < 0)
			{
				throw gcnew IOException("avio_open error");
			}
		}

		if (avformat_write_header(m_data->FormatContext, nullptr) < 0)
		{
			throw gcnew IOException("avformat_write_header error");
		}

		if (m_data->VideoCodecContext != nullptr)
		{
			m_data->VideoFrame = av_frame_alloc();
			if (m_data->VideoCodecContext->pix_fmt == AV_PIX_FMT_D3D11 || m_data->VideoCodecContext->pix_fmt ==
				AV_PIX_FMT_QSV)
			{
				if (av_hwframe_get_buffer(m_data->VideoCodecContext->hw_frames_ctx, m_data->VideoFrame, 0) < 0)
				{
					throw gcnew IOException("av_hwframe_get_buffer error");
				}

				m_data->SoftwareVideoFrame = av_frame_alloc();
				m_data->SoftwareVideoFrame->width = m_width;
				m_data->SoftwareVideoFrame->height = m_height;
				m_data->SoftwareVideoFrame->format = AV_PIX_FMT_NV12;
				if (av_frame_get_buffer(m_data->SoftwareVideoFrame, 32) < 0)
				{
					throw gcnew IOException("av_frame_get_buffer error (SoftwareVideoFrame)");
				}
				m_data->SwsSrcWidth = m_data->SoftwareVideoFrame->width;
				m_data->SwsSrcHeight = m_data->SoftwareVideoFrame->height;
				m_data->SwsSrcFormat = static_cast<AVPixelFormat>(m_data->SoftwareVideoFrame->format);
			}
			else
			{
				m_data->VideoFrame->width = m_width;
				m_data->VideoFrame->height = m_height;
				m_data->VideoFrame->format = m_data->VideoCodecContext->pix_fmt;
				if (av_frame_get_buffer(m_data->VideoFrame, 32) < 0)
				{
					throw gcnew IOException("av_frame_get_buffer error (VideoFrame)");
				}
				m_data->SwsSrcWidth = m_data->VideoFrame->width;
				m_data->SwsSrcHeight = m_data->VideoFrame->height;
				m_data->SwsSrcFormat = static_cast<AVPixelFormat>(m_data->VideoFrame->format);
			}
		}

		if (m_data->AudioCodecContext != nullptr)
		{
			m_data->AudioFrame = av_frame_alloc();
			m_data->AudioFrame->format = m_data->AudioCodecContext->sample_fmt;
			m_data->AudioFrame->channel_layout = m_data->AudioCodecContext->channel_layout;
			m_data->AudioFrame->sample_rate = m_data->AudioCodecContext->sample_rate;
			if (m_data->AudioCodecContext->codec->capabilities & AV_CODEC_CAP_VARIABLE_FRAME_SIZE)
				m_data->AudioFrame->nb_samples = 8096;
			else
				m_data->AudioFrame->nb_samples = m_data->AudioCodecContext->frame_size;
			av_frame_get_buffer(m_data->AudioFrame, 0);

			m_data->SwrContext = swr_alloc_set_opts(
				nullptr,
				m_data->AudioCodecContext->channel_layout,
				m_data->AudioCodecContext->sample_fmt,
				m_data->AudioCodecContext->sample_rate,
				AV_CH_LAYOUT_STEREO, AV_SAMPLE_FMT_S16, 48000, 0, nullptr);
			swr_init(m_data->SwrContext);
		}
	}

	void MediaWriter::Close()
	{
		if (m_data == nullptr)
			return;

		AVFormatContext* formatContext = m_data->FormatContext;

		if (m_data->VideoCodecContext != nullptr && m_data->VideoFrame != nullptr)
			write_frame(formatContext, m_data->VideoCodecContext, m_data->VideoStream, nullptr);
		if (m_data->AudioCodecContext != nullptr && m_data->AudioFrame != nullptr)
			write_frame(formatContext, m_data->AudioCodecContext, m_data->AudioStream, nullptr);
		if (m_data->AudioFrame != nullptr || m_data->VideoFrame != nullptr)
			av_write_trailer(formatContext);
		avformat_close_input(&formatContext);

		if (formatContext && !(formatContext->oformat->flags & AVFMT_NOFILE))
			avio_close(formatContext->pb);
		avcodec_close(m_data->VideoCodecContext);
		avcodec_close(m_data->AudioCodecContext);

		if (m_data->VideoFrame != nullptr)
			av_free(m_data->VideoFrame);
		if (m_data->AudioFrame != nullptr)
			av_free(m_data->AudioFrame);

		if (m_data->SwsContext != nullptr)
			sws_freeContext(m_data->SwsContext);
		if (m_data->SwrContext != nullptr)
		{
			SwrContext* c = m_data->SwrContext;
			swr_free(&c);
		}
		if (m_data->HardwareDeviceContext != nullptr)
		{
			AVBufferRef* h = m_data->HardwareDeviceContext;
			av_buffer_unref(&h);
		}

		m_data = nullptr;
	}

	void MediaWriter::EncodeVideoFrame(VideoFrame^ videoFrame)
	{
		if (m_data == nullptr || videoFrame == nullptr || videoFrame->NativePointer == IntPtr::Zero || m_data->
			VideoCodecContext == nullptr)
			return;

		auto avFrame = static_cast<AVFrame*>(videoFrame->NativePointer.ToPointer());

		if (avFrame->width != m_data->SwsSrcWidth || avFrame->height != m_data->SwsSrcHeight || avFrame->format !=
			m_data->SwsSrcFormat)
		{
			if (m_data->SwsContext != nullptr)
			{
				sws_freeContext(m_data->SwsContext);
				m_data->SwsContext = nullptr;
			}

			if (m_data->VideoCodecContext->hw_frames_ctx != nullptr)
			{
				if (avFrame->width != m_data->VideoCodecContext->width || avFrame->height != m_data->VideoCodecContext->
					height || avFrame->format != AV_PIX_FMT_NV12)
				{
					m_data->SwsContext = sws_getContext(avFrame->width, avFrame->height,
					                                    static_cast<AVPixelFormat>(avFrame->format),
					                                    m_data->VideoCodecContext->width,
					                                    m_data->VideoCodecContext->height, AV_PIX_FMT_NV12,
					                                    SWS_FAST_BILINEAR, nullptr, nullptr, nullptr);
				}
				m_data->SwsSrcWidth = avFrame->width;
				m_data->SwsSrcHeight = avFrame->height;
				m_data->SwsSrcFormat = static_cast<AVPixelFormat>(avFrame->format);
			}
			else
			{
				m_data->SwsContext = sws_getContext(avFrame->width, avFrame->height,
				                                    static_cast<AVPixelFormat>(avFrame->format),
				                                    m_data->VideoCodecContext->width, m_data->VideoCodecContext->height,
				                                    m_data->VideoCodecContext->pix_fmt,
				                                    SWS_FAST_BILINEAR, nullptr, nullptr, nullptr);
				m_data->SwsSrcWidth = avFrame->width;
				m_data->SwsSrcHeight = avFrame->height;
				m_data->SwsSrcFormat = static_cast<AVPixelFormat>(avFrame->format);
			}
		}

		if (m_data->VideoCodecContext->hw_frames_ctx != nullptr)
		{
			if (m_data->SwsContext != nullptr && (avFrame->width != m_data->VideoCodecContext->width || avFrame->height
				!= m_data->VideoCodecContext->height || avFrame->format != AV_PIX_FMT_NV12))
			{
				sws_scale(m_data->SwsContext, avFrame->data, avFrame->linesize, 0, avFrame->height,
				          m_data->SoftwareVideoFrame->data, m_data->SoftwareVideoFrame->linesize);
				av_hwframe_transfer_data(m_data->VideoFrame, m_data->SoftwareVideoFrame, 0);
			}
			else
			{
				av_hwframe_transfer_data(m_data->VideoFrame, avFrame, 0);
			}
		}
		else
		{
			if (m_data->SwsContext != nullptr && (avFrame->width != m_data->VideoCodecContext->width || avFrame->height
				!= m_data->VideoCodecContext->height || avFrame->format != m_data->VideoCodecContext->pix_fmt))
			{
				sws_scale(m_data->SwsContext, avFrame->data, avFrame->linesize, 0, avFrame->height,
				          m_data->VideoFrame->data, m_data->VideoFrame->linesize);
			}
			else
			{
				av_frame_copy(m_data->VideoFrame, avFrame);
			}
		}

		m_data->VideoFrame->pts = m_data->NextVideoPts++;
		write_frame(m_data->FormatContext, m_data->VideoCodecContext, m_data->VideoStream, m_data->VideoFrame);

		//printf("%d %d", m_data->VideoCodecContext->time_base, m_data->VideoStream->time_base);

		m_videoFramesCount++;
	}

	void MediaWriter::EncodeAudioFrame(AudioFrame^ audioFrame)
	{
		if (m_data == nullptr || audioFrame == nullptr || audioFrame->NativePointer == IntPtr::Zero || m_data->
			AudioCodecContext == nullptr)
			return;

		auto avFrame = static_cast<AVFrame*>(audioFrame->NativePointer.ToPointer());

		do
		{
			if (swr_convert_frame(m_data->SwrContext, m_data->AudioFrame, avFrame) < 0)
				break;

			m_data->AudioFrame->pts = m_data->NextAudioPts;
			m_data->NextAudioPts += m_data->AudioFrame->nb_samples;
			write_frame(m_data->FormatContext, m_data->AudioCodecContext, m_data->AudioStream, m_data->AudioFrame);
			m_audioSamplesCount += m_data->AudioFrame->nb_samples;
			avFrame = nullptr;
		}
		while (swr_get_delay(m_data->SwrContext, m_data->AudioFrame->sample_rate) >= (m_data->AudioFrame->nb_samples +
			32));
	}
}
