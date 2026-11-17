using System;
using System.Diagnostics;
using FFmpeg.AutoGen;

namespace MediaEncoder
{
    /// <summary>
    /// File muxer for mp4 / matroska / mov / avi / mpegts over FFmpeg.AutoGen. Streams are built
    /// from the encoders' codec parameters + extradata. Packets are written with
    /// av_interleaved_write_frame (FFmpeg orders by DTS — never hand-interleave); PTS/DTS are
    /// rescaled from the encoder time base to the stream time base. Either stream may be absent
    /// (audio-only recording has no video stream).
    ///
    /// MP4/MOV are written as "hybrid fragmented": the file is fragmented while recording, so a
    /// crash or power loss still leaves a playable file up to the last fragment, and on a normal
    /// stop FFmpeg turns it into a regular, universally compatible MP4 (no fragment structure
    /// visible to players or editors, and no whole-file rewrite like faststart needs).
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
        private AVPacket* _pkt;
        private bool _completed;
        private bool _completedOk;
        private bool _movFamily;
        private long _packetsWritten;
        private long _finalSize;
        private long _lastFlushTicks;
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

                if (video == null && audio == null)
                    throw new ArgumentException("A container needs at least one stream.");

                if (video != null)
                    InitializeVideoStream(video);
                if (audio != null)
                    InitializeAudioStream(audio);

                _movFamily = IsMovFamily(_formatShortName);
                if (_movFamily)
                {
                    if (video != null)
                    {
                        // A fragment per video keyframe (every ~2 s).
                        ffmpeg.av_dict_set(&options, "movflags", "+hybrid_fragmented+frag_keyframe", 0);
                    }
                    else
                    {
                        ffmpeg.av_dict_set(&options, "movflags", "+hybrid_fragmented", 0);
                        ffmpeg.av_dict_set(&options, "frag_duration", "2000000", 0); // microseconds
                    }
                }

                _pkt = ffmpeg.av_packet_alloc();

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
            // Variable frame rate: leave the average rate unset rather than claim the grid rate
            // (Matroska would otherwise write it as the default duration of every frame).
            _vStream->avg_frame_rate = video.VariableFrameRate
                ? new AVRational { num = 0, den = 1 }
                : _vCtx->framerate;
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
            // Samples per packet: muxers without per-packet timestamps (AVI) derive the audio
            // timeline from it.
            _aCtx->frame_size = audio.FrameSize;
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

        /// <summary>
        /// Bytes written to the output so far (0 when unknown); the size of the file once it
        /// has been finalized.
        /// </summary>
        public long BytesWritten
        {
            get
            {
                if (_completed)
                    return _finalSize;
                if (!_initialized || _fmt == null || _fmt->pb == null)
                    return 0;
                long position = ffmpeg.avio_tell(_fmt->pb);
                return position > 0 ? position : 0;
            }
        }

        /// <summary>Packets handed to the muxer so far (0 = the recording is empty).</summary>
        public long PacketsWritten => _packetsWritten;

        /// <summary>
        /// Pushes what the muxer produced out to the operating system, so that a crash or a
        /// power cut loses as little as possible. The I/O layer otherwise holds data back in
        /// a large buffer — at low bitrates for minutes.
        /// MP4 / MOV hand over data only when a fragment is complete, so flushing after every
        /// packet costs nothing there and puts each fragment on disk the moment it exists.
        /// The streaming formats produce data with every packet; those are flushed once a second.
        /// </summary>
        private void FlushIfDue()
        {
            _packetsWritten++;

            if (_fmt->pb == null)
                return;

            if (!_movFamily)
            {
                long now = Stopwatch.GetTimestamp();
                if (now - _lastFlushTicks < Stopwatch.Frequency)
                    return;
                _lastFlushTicks = now;
            }

            ffmpeg.avio_flush(_fmt->pb);
        }

        public static bool IsMovFamily(string formatShortName)
            => formatShortName == "mp4" || formatShortName == "mov" || formatShortName == "ipod";

        public void WriteVideoPacket(in EncodedVideoPacket packet)
        {
            if (_vStream == null)
                return;

            AVPacket* pkt = _pkt;
            ffmpeg.av_packet_unref(pkt);
            pkt->data = (byte*)packet.Data;
            pkt->size = packet.Size;
            pkt->stream_index = _vStreamIndex;
            pkt->pts = ffmpeg.av_rescale_q(packet.PresentationTimeStamp, _vCtx->time_base, _vStream->time_base);
            pkt->dts = ffmpeg.av_rescale_q(packet.DecodingTimeStamp, _vCtx->time_base, _vStream->time_base);
            if (packet.IsKeyFrame)
                pkt->flags |= ffmpeg.AV_PKT_FLAG_KEY;

            // The data is not reference-counted: av_interleaved_write_frame copies what it keeps.
            int result = ffmpeg.av_interleaved_write_frame(_fmt, pkt);
            if (result < 0)
                throw new InvalidOperationException($"av_interleaved_write_frame (video) failed: {FFmpegHelper.GetErrorString(result)}");

            FlushIfDue();
        }

        public void WriteAudioPacket(in EncodedAudioPacket packet)
        {
            if (_aStream == null)
                return;

            AVPacket* pkt = _pkt;
            ffmpeg.av_packet_unref(pkt);
            pkt->data = (byte*)packet.Data;
            pkt->size = packet.Size;
            pkt->stream_index = _aStreamIndex;
            pkt->pts = ffmpeg.av_rescale_q(packet.PresentationTimeStamp, _aCtx->time_base, _aStream->time_base);
            pkt->dts = ffmpeg.av_rescale_q(packet.DecodingTimeStamp, _aCtx->time_base, _aStream->time_base);
            pkt->flags |= ffmpeg.AV_PKT_FLAG_KEY;

            int result = ffmpeg.av_interleaved_write_frame(_fmt, pkt);
            if (result < 0)
                throw new InvalidOperationException($"av_interleaved_write_frame (audio) failed: {FFmpegHelper.GetErrorString(result)}");

            FlushIfDue();
        }

        /// <summary>
        /// Flushes the interleaving queue and writes the trailer (for MP4 this is what turns
        /// the fragmented file into a regular one). Returns false when finalizing failed —
        /// e.g. the disk is full — in which case the file is still playable up to the last
        /// fragment. Runs once; <see cref="Dispose"/> calls it if the caller did not.
        /// A recording without a single packet is never a success: there is nothing to play.
        /// </summary>
        public bool Complete()
        {
            if (!_initialized || _fmt == null)
                return false;
            if (_completed)
                return _completedOk;

            if (_packetsWritten == 0 && _movFamily)
            {
                // The MP4 muxer cannot finalize a file without any sample: its trailer code
                // dereferences sample tables that were never created and takes the whole
                // process down. Leave the header-only file as it is.
                _finalSize = CurrentFileSize();
                _completed = true;
                _completedOk = false;
                return false;
            }

            int flush = ffmpeg.av_interleaved_write_frame(_fmt, null);
            int trailer = ffmpeg.av_write_trailer(_fmt);
            if (_fmt->pb != null)
                ffmpeg.avio_flush(_fmt->pb);

            // Not avio_tell: finalizing an MP4 seeks back to the start to rewrite its header.
            _finalSize = CurrentFileSize();
            _completed = true;
            _completedOk = flush >= 0 && trailer >= 0 && _packetsWritten > 0;
            return _completedOk;
        }

        private long CurrentFileSize()
        {
            if (_fmt == null || _fmt->pb == null)
                return 0;

            long size = ffmpeg.avio_size(_fmt->pb);
            if (size > 0)
                return size;

            long position = ffmpeg.avio_tell(_fmt->pb);
            return position > 0 ? position : 0;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            Complete();

            if (_pkt != null)
            {
                fixed (AVPacket** pkt = &_pkt)
                    ffmpeg.av_packet_free(pkt);
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
