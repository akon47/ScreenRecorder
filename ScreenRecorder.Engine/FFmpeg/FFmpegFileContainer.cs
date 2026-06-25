using System;
using FFmpeg.AutoGen;

namespace MediaEncoder
{
    /// <summary>
    /// File muxer for mp4 / matroska / mov / avi / mpegts over FFmpeg.AutoGen. Streams are built
    /// from the encoders' codec parameters + extradata. Packets are written with
    /// av_interleaved_write_frame (FFmpeg orders by DTS — never hand-interleave); PTS/DTS are
    /// rescaled from the encoder time base to the stream time base.
    /// </summary>
    internal sealed unsafe class FFmpegFileContainer : IDisposable
    {
        private readonly string _formatShortName;
        private AVOutputFormat* _oformat;
        private AVFormatContext* _fmt;
        private AVCodecContext* _vCtx;
        private AVCodecContext* _aCtx;
        private AVStream* _vStream;
        private AVStream* _aStream;
        private int _vStreamIndex = -1;
        private int _aStreamIndex = -1;
        private bool _initialized;
        private bool _disposed;

        public bool GlobalHeader { get; }

        public FFmpegFileContainer(string formatShortName)
        {
            FFmpegBootstrap.EnsureInitialized();

            _formatShortName = formatShortName;
            _oformat = ffmpeg.av_guess_format(formatShortName, null, null);
            if (_oformat == null)
                throw new NotSupportedException($"Unknown container format '{formatShortName}'.");

            GlobalHeader = (_oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0;
        }

        public void Initialize(string url, FFmpegVideoEncoder video, FFmpegAudioEncoder audio)
        {
            AVDictionary* options = null;
            try
            {
                int allocResult;
                fixed (AVFormatContext** fmt = &_fmt)
                    allocResult = ffmpeg.avformat_alloc_output_context2(fmt, _oformat, null, url);
                if (allocResult < 0 || _fmt == null)
                    throw new InvalidOperationException($"avformat_alloc_output_context2 failed: {FFmpegHelper.GetErrorString(allocResult)}");

                InitializeVideoStream(video);
                if (audio != null)
                    InitializeAudioStream(audio);

                if (_formatShortName == "mp4" || _formatShortName == "mov")
                    ffmpeg.av_dict_set(&options, "movflags", "faststart", 0);

                if ((_oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
                {
                    int ioResult = ffmpeg.avio_open2(&_fmt->pb, url, ffmpeg.AVIO_FLAG_WRITE, null, null);
                    if (ioResult < 0)
                        throw new InvalidOperationException($"avio_open2 failed for '{url}': {FFmpegHelper.GetErrorString(ioResult)}");
                }

                int headerResult = ffmpeg.avformat_write_header(_fmt, &options);
                if (headerResult < 0)
                    throw new InvalidOperationException($"avformat_write_header failed: {FFmpegHelper.GetErrorString(headerResult)}");

                _initialized = true;
            }
            finally
            {
                if (options != null)
                    ffmpeg.av_dict_free(&options);
            }
        }

        private void InitializeVideoStream(FFmpegVideoEncoder video)
        {
            _vStream = ffmpeg.avformat_new_stream(_fmt, null);
            if (_vStream == null)
                throw new InvalidOperationException("avformat_new_stream (video) failed.");
            _vStreamIndex = _vStream->index;
            _vStream->id = (int)(_fmt->nb_streams - 1);

            _vCtx = ffmpeg.avcodec_alloc_context3(null);
            _vCtx->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
            _vCtx->codec_id = video.CodecId;
            _vCtx->bit_rate = video.Bitrate;
            _vCtx->width = video.Width;
            _vCtx->height = video.Height;
            _vCtx->time_base = new AVRational { num = video.FpsDenominator, den = video.FpsNumerator };
            _vCtx->framerate = new AVRational { num = video.FpsNumerator, den = video.FpsDenominator };
            _vCtx->pix_fmt = video.PixelFormat;
            if (GlobalHeader)
                _vCtx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

            if (video.HeaderData != 0 && video.HeaderDataSize > 0)
            {
                _vCtx->extradata = (byte*)ffmpeg.av_memdup((void*)video.HeaderData, (ulong)video.HeaderDataSize);
                _vCtx->extradata_size = video.HeaderDataSize;
            }

            _vStream->time_base = _vCtx->time_base;
            _vStream->avg_frame_rate = _vCtx->framerate;
            ffmpeg.avcodec_parameters_from_context(_vStream->codecpar, _vCtx);
        }

        private void InitializeAudioStream(FFmpegAudioEncoder audio)
        {
            _aStream = ffmpeg.avformat_new_stream(_fmt, null);
            if (_aStream == null)
                throw new InvalidOperationException("avformat_new_stream (audio) failed.");
            _aStreamIndex = _aStream->index;
            _aStream->id = (int)(_fmt->nb_streams - 1);

            _aCtx = ffmpeg.avcodec_alloc_context3(null);
            _aCtx->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
            _aCtx->codec_id = audio.CodecId;
            _aCtx->bit_rate = audio.Bitrate;
            _aCtx->sample_rate = audio.SampleRate;
            _aCtx->sample_fmt = audio.SampleFmt;
            _aCtx->time_base = new AVRational { num = 1, den = audio.SampleRate };
            ffmpeg.av_channel_layout_default(&_aCtx->ch_layout, audio.Channels);
            if (GlobalHeader)
                _aCtx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

            if (audio.HeaderData != 0 && audio.HeaderDataSize > 0)
            {
                _aCtx->extradata = (byte*)ffmpeg.av_memdup((void*)audio.HeaderData, (ulong)audio.HeaderDataSize);
                _aCtx->extradata_size = audio.HeaderDataSize;
            }

            _aStream->time_base = _aCtx->time_base;
            ffmpeg.avcodec_parameters_from_context(_aStream->codecpar, _aCtx);
        }

        public void WriteVideoPacket(in EncodedVideoPacket packet)
        {
            AVPacket* pkt = ffmpeg.av_packet_alloc();
            try
            {
                pkt->data = (byte*)packet.Data;
                pkt->size = packet.Size;
                pkt->stream_index = _vStreamIndex;
                pkt->pts = ffmpeg.av_rescale_q(packet.PresentationTimeStamp, _vCtx->time_base, _vStream->time_base);
                pkt->dts = ffmpeg.av_rescale_q(packet.DecodingTimeStamp, _vCtx->time_base, _vStream->time_base);
                if (packet.IsKeyFrame)
                    pkt->flags |= ffmpeg.AV_PKT_FLAG_KEY;

                int result = ffmpeg.av_interleaved_write_frame(_fmt, pkt);
                if (result < 0)
                    throw new InvalidOperationException($"av_interleaved_write_frame (video) failed: {FFmpegHelper.GetErrorString(result)}");
            }
            finally
            {
                ffmpeg.av_packet_free(&pkt);
            }
        }

        public void WriteAudioPacket(in EncodedAudioPacket packet)
        {
            AVPacket* pkt = ffmpeg.av_packet_alloc();
            try
            {
                pkt->data = (byte*)packet.Data;
                pkt->size = packet.Size;
                pkt->stream_index = _aStreamIndex;
                pkt->pts = ffmpeg.av_rescale_q(packet.PresentationTimeStamp, _aCtx->time_base, _aStream->time_base);
                pkt->dts = ffmpeg.av_rescale_q(packet.DecodingTimeStamp, _aCtx->time_base, _aStream->time_base);

                int result = ffmpeg.av_interleaved_write_frame(_fmt, pkt);
                if (result < 0)
                    throw new InvalidOperationException($"av_interleaved_write_frame (audio) failed: {FFmpegHelper.GetErrorString(result)}");
            }
            finally
            {
                ffmpeg.av_packet_free(&pkt);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_initialized && _fmt != null)
            {
                // Flush the interleaving queue then finalize (writes the moov; faststart relocates it).
                ffmpeg.av_interleaved_write_frame(_fmt, null);
                ffmpeg.av_write_trailer(_fmt);
            }

            if (_vCtx != null)
            {
                fixed (AVCodecContext** ctx = &_vCtx)
                    ffmpeg.avcodec_free_context(ctx);
            }

            if (_aCtx != null)
            {
                fixed (AVCodecContext** ctx = &_aCtx)
                    ffmpeg.avcodec_free_context(ctx);
            }

            if (_fmt != null)
            {
                if ((_oformat->flags & ffmpeg.AVFMT_NOFILE) == 0 && _fmt->pb != null)
                {
                    ffmpeg.avio_closep(&_fmt->pb);
                }

                ffmpeg.avformat_free_context(_fmt);
                _fmt = null;
            }
        }
    }
}
