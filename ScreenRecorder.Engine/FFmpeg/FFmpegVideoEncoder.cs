using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace MediaEncoder
{
    /// <summary>
    /// H.264 / H.265 video encoder over FFmpeg.AutoGen, supporting NVENC, QuickSync and software
    /// (libx264/libx265). Input is NV12 planes; if the chosen encoder does not accept NV12 a
    /// swscale pass converts to its native pixel format. PTS is the caller-supplied ideal-grid
    /// frame index (encoder time_base = 1/fps, CFR). The returned packet's <c>Data</c> is valid
    /// only until the next Receive/Send call — copy it before reusing the encoder.
    /// </summary>
    internal sealed unsafe class FFmpegVideoEncoder : IDisposable
    {
        private AVCodec* _codec;
        private AVCodecContext* _ctx;
        private AVFrame* _frame;
        private SwsContext* _sws;
        private AVPacket* _packet;
        private nint _buffer;
        private int _bufferSize;
        private nint _headerData;
        private int _headerDataSize;
        private readonly int _width;
        private readonly int _height;
        private bool _disposed;

        public AVCodecID CodecId => _ctx->codec_id;
        public int Width => _width;
        public int Height => _height;
        public AVPixelFormat PixelFormat => _ctx->pix_fmt;
        public int FpsNumerator { get; }
        public int FpsDenominator { get; }
        public long Bitrate => _ctx->bit_rate;
        public nint HeaderData => _headerData;
        public int HeaderDataSize => _headerDataSize;

        public FFmpegVideoEncoder(
            VideoCodec codec,
            HwAccel hw,
            int width,
            int height,
            int fpsNumerator,
            int fpsDenominator,
            int bitrateBps,
            RateControl rateControl = RateControl.Cbr,
            bool globalHeader = true,
            string presetOverride = null)
        {
            FFmpegBootstrap.EnsureInitialized();

            _width = width;
            _height = height;
            FpsNumerator = fpsNumerator;
            FpsDenominator = fpsDenominator;

            foreach (var name in ResolveEncoderNames(codec, hw))
            {
                _codec = ffmpeg.avcodec_find_encoder_by_name(name);
                if (_codec != null)
                    break;
            }

            if (_codec == null)
                throw new NotSupportedException($"No usable {codec} encoder for {hw} (and no software fallback found).");

            _ctx = ffmpeg.avcodec_alloc_context3(_codec);
            if (_ctx == null)
                throw new InvalidOperationException("avcodec_alloc_context3 failed for video encoder.");

            _ctx->bit_rate = bitrateBps;
            _ctx->width = width;
            _ctx->height = height;
            _ctx->time_base = new AVRational { num = fpsDenominator, den = fpsNumerator };
            _ctx->framerate = new AVRational { num = fpsNumerator, den = fpsDenominator };
            _ctx->gop_size = (int)(2.0 * fpsNumerator / fpsDenominator);
            _ctx->pix_fmt = SelectPixelFormat(_ctx, _codec);

            if (globalHeader)
                _ctx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

            InitializeCodec(hw, rateControl, bitrateBps, presetOverride);

            int openResult = ffmpeg.avcodec_open2(_ctx, _codec, null);
            if (openResult < 0)
                throw new InvalidOperationException($"avcodec_open2 (video) failed: {FFmpegHelper.GetErrorString(openResult)}");

            // Capture extradata (SPS/PPS or VPS/SPS/PPS) for the muxer's stream codecpar.
            if (_ctx->extradata != null && _ctx->extradata_size > 0)
            {
                _headerDataSize = _ctx->extradata_size;
                _headerData = Marshal.AllocHGlobal(_headerDataSize);
                Unsafe.CopyBlockUnaligned((void*)_headerData, _ctx->extradata, (uint)_headerDataSize);
            }

            // Reusable input frame (encoder pixel format).
            _frame = ffmpeg.av_frame_alloc();
            _frame->format = (int)_ctx->pix_fmt;
            _frame->width = width;
            _frame->height = height;
            _frame->color_range = _ctx->color_range;
            _frame->color_primaries = _ctx->color_primaries;
            _frame->color_trc = _ctx->color_trc;
            _frame->colorspace = _ctx->colorspace;
            _frame->chroma_location = _ctx->chroma_sample_location;
            int bufResult = ffmpeg.av_frame_get_buffer(_frame, 32);
            if (bufResult < 0)
                throw new InvalidOperationException($"av_frame_get_buffer (video) failed: {FFmpegHelper.GetErrorString(bufResult)}");

            // If the encoder does not take NV12 directly, set up a converter (e.g. libx265 -> yuv420p).
            if (_ctx->pix_fmt != AVPixelFormat.AV_PIX_FMT_NV12)
            {
                const int SWS_BILINEAR = 2; // FFmpeg.AutoGen 8.1.0 does not expose the SWS_* flag constants.
                _sws = ffmpeg.sws_getContext(
                    width, height, AVPixelFormat.AV_PIX_FMT_NV12,
                    width, height, _ctx->pix_fmt,
                    SWS_BILINEAR, null, null, null);
                if (_sws == null)
                    throw new InvalidOperationException("sws_getContext (NV12 conversion) failed.");
            }

            _packet = ffmpeg.av_packet_alloc();
        }

        private static string[] ResolveEncoderNames(VideoCodec codec, HwAccel hw)
        {
            switch (codec)
            {
                case VideoCodec.H264:
                    return hw switch
                    {
                        HwAccel.Nvenc => new[] { "h264_nvenc", "nvenc_h264" },
                        HwAccel.Qsv => new[] { "h264_qsv" },
                        _ => new[] { "libx264" },
                    };
                case VideoCodec.Hevc:
                    return hw switch
                    {
                        HwAccel.Nvenc => new[] { "hevc_nvenc", "nvenc_hevc" },
                        HwAccel.Qsv => new[] { "hevc_qsv" },
                        _ => new[] { "libx265" },
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported video codec");
            }
        }

        private static AVPixelFormat SelectPixelFormat(AVCodecContext* ctx, AVCodec* codec)
        {
            AVPixelFormat* formats = null;
            int count = 0;
            int result = ffmpeg.avcodec_get_supported_config(
                ctx, codec, AVCodecConfig.AV_CODEC_CONFIG_PIX_FORMAT, 0, (void**)&formats, &count);

            if (result < 0 || formats == null || count <= 0)
                return AVPixelFormat.AV_PIX_FMT_NV12;

            AVPixelFormat fallback = formats[0];
            for (int i = 0; i < count; i++)
            {
                if (formats[i] == AVPixelFormat.AV_PIX_FMT_NV12)
                    return AVPixelFormat.AV_PIX_FMT_NV12;
            }

            return fallback;
        }

        private void InitializeCodec(HwAccel hw, RateControl rateControl, int bitrate, string presetOverride)
        {
            void* priv = _ctx->priv_data;
            bool cbr = rateControl == RateControl.Cbr;

            switch (hw)
            {
                case HwAccel.Nvenc:
                    _ctx->max_b_frames = 0;
                    ffmpeg.av_opt_set_int(priv, "cbr", cbr ? 1 : 0, 0);
                    if (cbr)
                    {
                        _ctx->rc_min_rate = bitrate;
                        _ctx->rc_max_rate = bitrate;
                        _ctx->rc_buffer_size = bitrate;
                    }
                    ffmpeg.av_opt_set(priv, "preset", presetOverride ?? "p4", 0);
                    ffmpeg.av_opt_set(priv, "tune", "ll", 0);
                    break;

                case HwAccel.Qsv:
                    _ctx->max_b_frames = 0;
                    if (cbr)
                    {
                        _ctx->rc_min_rate = bitrate;
                        _ctx->rc_max_rate = bitrate;
                        _ctx->rc_buffer_size = bitrate;
                    }
                    else
                    {
                        _ctx->rc_max_rate = bitrate * 2L;
                        _ctx->rc_buffer_size = bitrate * 2;
                    }
                    ffmpeg.av_opt_set(priv, "preset", presetOverride ?? "veryfast", 0);
                    break;

                default: // Software (libx264 / libx265)
                    _ctx->max_b_frames = 0;
                    ffmpeg.av_opt_set(priv, "preset", presetOverride ?? "veryfast", 0);
                    ffmpeg.av_opt_set(priv, "tune", "zerolatency", 0);
                    if (cbr)
                    {
                        _ctx->rc_min_rate = bitrate;
                        _ctx->rc_max_rate = bitrate;
                        _ctx->rc_buffer_size = bitrate;
                    }
                    break;
            }
        }

        /// <summary>
        /// Copies one NV12 frame into the encoder input and submits it. <paramref name="ptsGridIndex"/>
        /// is the CFR frame index (PTS in the 1/fps time base).
        /// </summary>
        public void SendFrame(byte** srcPlanes, int* srcLinesizes, int planeCount, long ptsGridIndex)
        {
            ffmpeg.av_frame_make_writable(_frame);

            if (_sws == null)
            {
                CopyNv12(srcPlanes, srcLinesizes);
            }
            else
            {
                var srcData = new byte_ptrArray8();
                var srcStride = new int_array8();
                for (uint i = 0; i < planeCount && i < 8; i++)
                {
                    srcData[i] = srcPlanes[i];
                    srcStride[i] = srcLinesizes[i];
                }

                ffmpeg.sws_scale(_sws, srcData, srcStride, 0, _height, _frame->data, _frame->linesize);
            }

            _frame->pts = ptsGridIndex;

            int result = ffmpeg.avcodec_send_frame(_ctx, _frame);
            if (result < 0 && result != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                throw new InvalidOperationException($"avcodec_send_frame (video) failed: {FFmpegHelper.GetErrorString(result)}");
        }

        private void CopyNv12(byte** srcPlanes, int* srcLinesizes)
        {
            // NV12: plane 0 = Y (full height), plane 1 = interleaved UV (half height).
            int hShift, vShift;
            ffmpeg.av_pix_fmt_get_chroma_sub_sample((AVPixelFormat)_frame->format, &hShift, &vShift);

            for (int p = 0; p < 2; p++)
            {
                int rows = p == 0 ? _height : _height >> vShift;
                int dstStride = _frame->linesize[(uint)p];
                int srcStride = srcLinesizes[p];
                int bytes = Math.Min(srcStride, dstStride);
                byte* src = srcPlanes[p];
                byte* dst = _frame->data[(uint)p];

                for (int y = 0; y < rows; y++)
                {
                    Buffer.MemoryCopy(src + (long)y * srcStride, dst + (long)y * dstStride, dstStride, bytes);
                }
            }
        }

        /// <summary>Pulls the next encoded packet, or null if the encoder needs more input / is drained.</summary>
        public EncodedVideoPacket? ReceivePacket()
        {
            int result = ffmpeg.avcodec_receive_packet(_ctx, _packet);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
                return null;
            if (result < 0)
                throw new InvalidOperationException($"avcodec_receive_packet (video) failed: {FFmpegHelper.GetErrorString(result)}");

            try
            {
                if (_packet->size > _bufferSize)
                {
                    _buffer = _buffer == 0
                        ? Marshal.AllocHGlobal(_packet->size)
                        : Marshal.ReAllocHGlobal(_buffer, _packet->size);
                    _bufferSize = _packet->size;
                }

                Unsafe.CopyBlockUnaligned((void*)_buffer, _packet->data, (uint)_packet->size);

                bool isKey = (_packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
                return new EncodedVideoPacket(_packet->pts, _packet->dts, _buffer, _packet->size, isKey);
            }
            finally
            {
                ffmpeg.av_packet_unref(_packet);
            }
        }

        /// <summary>Signals end-of-stream so buffered frames can be drained via ReceivePacket.</summary>
        public void FlushSend()
        {
            ffmpeg.avcodec_send_frame(_ctx, null);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_ctx != null)
            {
                fixed (AVCodecContext** ctx = &_ctx)
                    ffmpeg.avcodec_free_context(ctx);
            }

            if (_frame != null)
            {
                fixed (AVFrame** frame = &_frame)
                    ffmpeg.av_frame_free(frame);
            }

            if (_packet != null)
            {
                fixed (AVPacket** packet = &_packet)
                    ffmpeg.av_packet_free(packet);
            }

            if (_sws != null)
            {
                ffmpeg.sws_freeContext(_sws);
                _sws = null;
            }

            if (_buffer != 0)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = 0;
            }

            if (_headerData != 0)
            {
                Marshal.FreeHGlobal(_headerData);
                _headerData = 0;
            }
        }
    }
}
