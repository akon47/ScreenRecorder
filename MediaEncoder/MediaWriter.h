#pragma once

using namespace System;
using namespace Collections::Generic;
using namespace IO;
using namespace Runtime::InteropServices;

#include "VideoFrame.h"
#include "AudioFrame.h"

namespace MediaEncoder
{
	ref struct WriterPrivateData;

	public ref class MediaWriter : IDisposable
	{
	private:
		static bool h264_nvenc = false;
		static bool h264_qsv = false;
		static bool hevc_nvenc = false;
		static bool hevc_qsv = false;

		static bool IsValidEncoder(const char* name)
		{
			const AVCodec* videoCodec = avcodec_find_encoder_by_name(name);
			if (videoCodec)
			{
				AVCodecContext* videoCodecContext = avcodec_alloc_context3(videoCodec);
				try
				{
					videoCodecContext->width = 1920;
					videoCodecContext->height = 1080;
					videoCodecContext->sample_aspect_ratio = av_make_q(1, 1);
					if (videoCodec->pix_fmts)
					{
						videoCodecContext->pix_fmt = videoCodec->pix_fmts[0];
						for (int i = 0; videoCodec->pix_fmts[i] != AV_PIX_FMT_NONE; i++)
						{
							if (videoCodec->pix_fmts[i] != AV_PIX_FMT_D3D11)
							{
								videoCodecContext->pix_fmt = videoCodec->pix_fmts[i];
								break;
							}
						}
					}
					else
					{
						videoCodecContext->pix_fmt = AV_PIX_FMT_YUV420P;
					}
					videoCodecContext->time_base = av_make_q(1, 60);
					videoCodecContext->framerate = av_make_q(60, 1);
					videoCodecContext->bit_rate = 10000000;

					if (avcodec_open2(videoCodecContext, videoCodec, nullptr) < 0)
					{
						return false;
					}
					return true;
				}
				finally
				{
					avcodec_close(videoCodecContext);
				}
			}
			return false;
		}

	public:
		static void CheckHardwareCodec()
		{
#ifndef NDEBUG
			av_log_set_level(AV_LOG_TRACE);
#endif

			h264_nvenc = IsValidEncoder("h264_nvenc");
			h264_qsv = IsValidEncoder("h264_qsv");
			hevc_nvenc = IsValidEncoder("hevc_nvenc");
			hevc_qsv = IsValidEncoder("hevc_qsv");

			av_log(nullptr, AV_LOG_INFO, h264_nvenc ? "h264_nvenc is supported\n" : "h264_nvenc is not supported\n");
			av_log(nullptr, AV_LOG_INFO, h264_qsv ? "h264_qsv is supported\n" : "h264_qsv is not supported\n");
			av_log(nullptr, AV_LOG_INFO, hevc_nvenc ? "hevc_nvenc is supported\n" : "hevc_nvenc is not supported\n");
			av_log(nullptr, AV_LOG_INFO, hevc_qsv ? "hevc_qsv is supported\n" : "hevc_qsv is not supported\n");
		}

		static bool IsSupportedNvencH264() { return h264_nvenc; }
		static bool IsSupportedNvencHEVC() { return hevc_nvenc; }
		static bool IsSupportedQsvH264() { return h264_qsv; }
		static bool IsSupportedQsvHEVC() { return hevc_qsv; }
	private:
		int m_width;
		int m_height;
		int m_videoNumerator;
		int m_videoDenominator;
		String^ m_videoCodecName;
		uint64_t m_videoFramesCount;
		int m_videoBitrate;
		AVCodecID m_videoCodec;
	private:
		int m_audioSampleRate;
		AVSampleFormat m_audioSampleFormat;
		String^ m_audioCodecName;
		uint64_t m_audioSamplesCount;
		int m_audioBitrate;
		AVCodecID m_audioCodec;
	private:
		String^ m_format;
		String^ m_url;

		WriterPrivateData^ m_data;
		bool m_disposed;

		void CheckIfWriterIsInitialized()
		{
			if (m_data == nullptr)
				throw gcnew IOException("Video file is not open, so can not access its properties.");
		}

		void CheckIfDisposed()
		{
			if (m_disposed)
				throw gcnew ObjectDisposedException("The object was already disposed.");
		}

	protected:
		!MediaWriter()
		{
			Close();
		}

	public:
		property uint64_t VideoFramesCount
		{
			uint64_t get()
			{
				CheckIfWriterIsInitialized();
				return m_videoFramesCount;
			}
		}

		property uint64_t AudioSamplesCount
		{
			uint64_t get()
			{
				CheckIfWriterIsInitialized();
				return m_audioSamplesCount;
			}
		}

		property int Width
		{
			int get()
			{
				CheckIfWriterIsInitialized();
				return m_width;
			}
		}

		property int Height
		{
			int get()
			{
				CheckIfWriterIsInitialized();
				return m_height;
			}
		}

		property int VideoNumerator
		{
			int get()
			{
				CheckIfWriterIsInitialized();
				return m_videoNumerator;
			}
		}

		property int VideoDenominator
		{
			int get()
			{
				CheckIfWriterIsInitialized();
				return m_videoDenominator;
			}
		}

		property int SampleRate
		{
			int get()
			{
				CheckIfWriterIsInitialized();
				return m_audioSampleRate;
			}
		}

		property String^ VideoCodecName
		{
			String^ get()
			{
				CheckIfWriterIsInitialized();
				return m_videoCodecName;
			}
		}

		property String^ AudioCodecName
		{
			String^ get()
			{
				CheckIfWriterIsInitialized();
				return m_audioCodecName;
			}
		}

		property bool IsInitialized
		{
			bool get()
			{
				return (m_data != nullptr);
			}
		}

		MediaWriter(
			int width, int height, int video_numerator, int video_denominator, VideoCodec video_codec,
			int video_bitrate,
			AudioCodec audio_codec, int audio_bitrate);

		~MediaWriter()
		{
			this->!MediaWriter();
			m_disposed = true;
		}

		void Open(String^ url, String^ format, bool forceSoftwareEncoder);

		void Open(String^ url, String^ format)
		{
			Open(url, format, false);
		}

		void Close();

		void EncodeVideoFrame(VideoFrame^ videoFrame);
		void EncodeAudioFrame(AudioFrame^ audioFrame);
	};
}
