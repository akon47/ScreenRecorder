using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using ScreenRecorder.DirectX;

namespace MediaEncoder
{
    /// <summary>Everything needed to open a <see cref="FFmpegVideoEncoder"/>.</summary>
    internal sealed class VideoEncoderSettings
    {
        public VideoCodec Codec;
        public HwAccel Hw;
        public int Width;
        public int Height;
        public int FpsNumerator;
        public int FpsDenominator = 1;
        public int Bitrate;
        public RateControl RateControl = RateControl.Cbr;
        public int Quality = 23;
        public bool GlobalHeader = true;
        public string PresetOverride;

        /// <summary>
        /// Frames arrive only when the content changed (PTS gaps on the grid). Affects stream
        /// metadata only — the encoder time base is the same 1/fps grid either way.
        /// </summary>
        public bool VariableFrameRate;

        /// <summary>
        /// When set, the encoder takes its input as GPU textures from this ring (zero-copy
        /// hardware path) instead of CPU NV12 buffers.
        /// </summary>
        public SharedNv12Ring SharedRing;
    }

    /// <summary>
    /// H.264 / H.265 / AV1 video encoder over FFmpeg.AutoGen: NVENC, Quick Sync, AMF or
    /// software (libx264 / libx265 / SVT-AV1). Input is either CPU NV12 (converted by swscale when the encoder does
    /// not take NV12) or, on the zero-copy path, shared GPU textures. PTS is the caller-supplied
    /// ideal-grid index in a 1/fps time base; gaps between indices are legal (variable frame
    /// rate), so keyframes are forced by elapsed grid time rather than by frame count.
    ///
    /// Usage per frame: one of the Prepare* calls, then <see cref="TrySendPrepared"/>; when it
    /// returns false the encoder is full — drain <see cref="TryReceive"/> and send again.
    /// </summary>
    internal sealed unsafe class FFmpegVideoEncoder : IDisposable
    {
        private AVCodec* _codec;
        private AVCodecContext* _ctx;
        private AVFrame* _frame;      // CPU input (encoder pixel format)
        private AVFrame* _hwFrame;    // GPU input (holds the last pool texture for repeats)
        private AVFrame* _prepared;   // whichever of the two is ready to send
        private SwsContext* _sws;
        private AVPacket* _packet;
        private SharedTextureInput _gpuInput;
        private nint _headerData;
        private int _headerDataSize;
        private readonly int _width;
        private readonly int _height;
        private readonly long _keyIntervalTicks;
        private long _lastKeyPts = long.MinValue;
        private bool _hasPicture;
        private bool _disposed;

        public AVCodecID CodecId => _ctx->codec_id;
        public int Width => _width;
        public int Height => _height;
        public AVPixelFormat PixelFormat => _ctx->pix_fmt;
        public int FpsNumerator { get; }
        public int FpsDenominator { get; }
        public bool VariableFrameRate { get; }
        public long Bitrate => _ctx->bit_rate;
        public nint HeaderData => _headerData;
        public int HeaderDataSize => _headerDataSize;

        /// <summary>True when input is shared GPU textures (the zero-copy path).</summary>
        public bool UsesGpuInput => _gpuInput != null;

        /// <summary>Name of the FFmpeg encoder in use (e.g. <c>h264_nvenc</c>, <c>libx264</c>).</summary>
        public string EncoderName { get; private set; }

        public FFmpegVideoEncoder(VideoEncoderSettings s)
        {
            FFmpegBootstrap.EnsureInitialized();

            _width = s.Width;
            _height = s.Height;
            FpsNumerator = s.FpsNumerator;
            FpsDenominator = s.FpsDenominator;
            VariableFrameRate = s.VariableFrameRate;
            _keyIntervalTicks = Math.Max(1, (long)Math.Ceiling(2.0 * s.FpsNumerator / s.FpsDenominator));

            try
            {
                Open(s);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Open(VideoEncoderSettings s)
        {
            foreach (var name in ResolveEncoderNames(s.Codec, s.Hw))
            {
                _codec = ffmpeg.avcodec_find_encoder_by_name(name);
                if (_codec != null)
                {
                    EncoderName = name;
                    break;
                }
            }

            if (_codec == null)
                throw new NotSupportedException($"No usable {s.Codec} encoder for {s.Hw}.");

            _ctx = ffmpeg.avcodec_alloc_context3(_codec);
            if (_ctx == null)
                throw new InvalidOperationException("avcodec_alloc_context3 failed for video encoder.");

            _ctx->bit_rate = s.Bitrate;
            _ctx->width = s.Width;
            _ctx->height = s.Height;
            _ctx->time_base = new AVRational { num = s.FpsDenominator, den = s.FpsNumerator };
            _ctx->framerate = new AVRational { num = s.FpsNumerator, den = s.FpsDenominator };
            _ctx->gop_size = (int)_keyIntervalTicks;

            // Tag what the VideoProcessor actually produces (limited range; BT.709 for HD,
            // BT.601 below) so players do not have to guess the matrix.
            bool bt709 = FrameConverter.IsBt709Output(s.Height);
            _ctx->color_range = AVColorRange.AVCOL_RANGE_MPEG;
            _ctx->colorspace = bt709 ? AVColorSpace.AVCOL_SPC_BT709 : AVColorSpace.AVCOL_SPC_SMPTE170M;
            _ctx->color_primaries = bt709 ? AVColorPrimaries.AVCOL_PRI_BT709 : AVColorPrimaries.AVCOL_PRI_SMPTE170M;
            _ctx->color_trc = bt709 ? AVColorTransferCharacteristic.AVCOL_TRC_BT709 : AVColorTransferCharacteristic.AVCOL_TRC_SMPTE170M;

            if (s.SharedRing != null)
            {
                _gpuInput = new SharedTextureInput(s.SharedRing);
                _ctx->pix_fmt = AVPixelFormat.AV_PIX_FMT_D3D11;
                _ctx->sw_pix_fmt = AVPixelFormat.AV_PIX_FMT_NV12;
                _ctx->hw_frames_ctx = ffmpeg.av_buffer_ref(_gpuInput.FramesContext);
            }
            else
            {
                _ctx->pix_fmt = SelectPixelFormat(_ctx, _codec);
            }

            if (s.GlobalHeader)
                _ctx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

            InitializeCodec(s.Codec, s.Hw, s.RateControl, s.Bitrate, s.Quality, s.PresetOverride);

            int openResult = ffmpeg.avcodec_open2(_ctx, _codec, null);
            if (openResult < 0)
                throw new InvalidOperationException($"avcodec_open2 (video, {EncoderName}) failed: {FFmpegHelper.GetErrorString(openResult)}");

            // Capture extradata (SPS/PPS or VPS/SPS/PPS) for the muxer's stream codecpar.
            if (_ctx->extradata != null && _ctx->extradata_size > 0)
            {
                _headerDataSize = _ctx->extradata_size;
                _headerData = Marshal.AllocHGlobal(_headerDataSize);
                Unsafe.CopyBlockUnaligned((void*)_headerData, _ctx->extradata, (uint)_headerDataSize);
            }

            _packet = ffmpeg.av_packet_alloc();

            if (_gpuInput != null)
            {
                _hwFrame = ffmpeg.av_frame_alloc();
                return;
            }

            // Reusable CPU input frame (encoder pixel format).
            _frame = ffmpeg.av_frame_alloc();
            _frame->format = (int)_ctx->pix_fmt;
            _frame->width = s.Width;
            _frame->height = s.Height;
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
                _sws = ffmpeg.sws_getContext(
                    s.Width, s.Height, AVPixelFormat.AV_PIX_FMT_NV12,
                    s.Width, s.Height, _ctx->pix_fmt,
                    (int)SwsFlags.SWS_BILINEAR, null, null, null);
                if (_sws == null)
                    throw new InvalidOperationException("sws_getContext (NV12 conversion) failed.");
            }
        }

        internal static string[] ResolveEncoderNames(VideoCodec codec, HwAccel hw)
        {
            switch (codec)
            {
                case VideoCodec.H264:
                    return hw switch
                    {
                        HwAccel.Nvenc => new[] { "h264_nvenc" },
                        HwAccel.Qsv => new[] { "h264_qsv" },
                        HwAccel.Amf => new[] { "h264_amf" },
                        _ => new[] { "libx264" },
                    };
                case VideoCodec.Hevc:
                    return hw switch
                    {
                        HwAccel.Nvenc => new[] { "hevc_nvenc" },
                        HwAccel.Qsv => new[] { "hevc_qsv" },
                        HwAccel.Amf => new[] { "hevc_amf" },
                        _ => new[] { "libx265" },
                    };
                case VideoCodec.Av1:
                    return hw switch
                    {
                        HwAccel.Nvenc => new[] { "av1_nvenc" },
                        HwAccel.Qsv => new[] { "av1_qsv" },
                        HwAccel.Amf => new[] { "av1_amf" },
                        _ => new[] { "libsvtav1" },
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

        private void InitializeCodec(VideoCodec codec, HwAccel hw, RateControl rateControl, int bitrate, int quality, string presetOverride)
        {
            void* priv = _ctx->priv_data;
            bool cbr = rateControl == RateControl.Cbr;
            bool cq = rateControl == RateControl.Cq;

            _ctx->max_b_frames = 0; // PTS == DTS; nothing to reorder

            if (cq)
            {
                // Constant quality: the bitrate follows content complexity.
                _ctx->bit_rate = 0;
            }

            // A forced I picture must be a real IDR so every forced keyframe is a seek point.
            // Unknown options are ignored (each encoder spells it its own way).
            ffmpeg.av_opt_set_int(priv, "forced-idr", 1, 0);
            ffmpeg.av_opt_set_int(priv, "forced_idr", 1, 0);

            switch (hw)
            {
                case HwAccel.Nvenc:
                    // "rc" is understood by every NVENC encoder (H.264, HEVC and AV1).
                    ffmpeg.av_opt_set(priv, "rc", cbr ? "cbr" : "vbr", 0);
                    if (cbr)
                    {
                        _ctx->rc_min_rate = bitrate;
                        _ctx->rc_max_rate = bitrate;
                        _ctx->rc_buffer_size = bitrate;
                    }
                    else if (cq)
                    {
                        // ffmpeg CLI equivalent: -rc vbr -cq <q> -b:v 0
                        ffmpeg.av_opt_set_int(priv, "cq", quality, 0);
                    }
                    ffmpeg.av_opt_set(priv, "preset", presetOverride ?? "p4", 0);
                    // File recording, not live streaming: spend the bits on quality.
                    ffmpeg.av_opt_set(priv, "tune", "hq", 0);
                    break;

                case HwAccel.Qsv:
                    if (cbr)
                    {
                        _ctx->rc_min_rate = bitrate;
                        _ctx->rc_max_rate = bitrate;
                        _ctx->rc_buffer_size = bitrate;
                    }
                    else if (cq)
                    {
                        // ICQ mode (no bitrate + global_quality). The shell prefers NVENC or
                        // software for constant quality; this path is a best-effort fallback.
                        _ctx->global_quality = quality;
                    }
                    else
                    {
                        _ctx->rc_max_rate = bitrate * 2L;
                        _ctx->rc_buffer_size = bitrate * 2;
                    }
                    ffmpeg.av_opt_set(priv, "preset", presetOverride ?? "veryfast", 0);
                    break;

                case HwAccel.Amf:
                    if (cq)
                    {
                        ffmpeg.av_opt_set(priv, "rc", "cqp", 0);
                        ffmpeg.av_opt_set_int(priv, "qp_i", quality, 0);
                        ffmpeg.av_opt_set_int(priv, "qp_p", quality, 0);
                    }
                    else
                    {
                        ffmpeg.av_opt_set(priv, "rc", cbr ? "cbr" : "vbr_peak", 0);
                        _ctx->rc_max_rate = cbr ? bitrate : bitrate * 2L;
                        _ctx->rc_buffer_size = bitrate;
                    }
                    ffmpeg.av_opt_set(priv, "quality", presetOverride ?? "balanced", 0);
                    break;

                default: // Software
                    if (codec == VideoCodec.Av1)
                    {
                        // SVT-AV1: its fastest presets are the only ones usable in real time.
                        // Constant quality uses its own 0-63 scale (roughly x264 CRF + 12).
                        ffmpeg.av_opt_set(priv, "preset", presetOverride ?? "11", 0);
                        if (cq)
                            ffmpeg.av_opt_set_int(priv, "crf", Math.Clamp(quality + 12, 1, 63), 0);
                        break;
                    }

                    // libx264 / libx265
                    ffmpeg.av_opt_set(priv, "preset", presetOverride ?? "veryfast", 0);
                    ffmpeg.av_opt_set(priv, "tune", "zerolatency", 0);
                    if (cbr)
                    {
                        _ctx->rc_min_rate = bitrate;
                        _ctx->rc_max_rate = bitrate;
                        _ctx->rc_buffer_size = bitrate;
                    }
                    else if (cq)
                    {
                        ffmpeg.av_opt_set(priv, "crf", quality.ToString(System.Globalization.CultureInfo.InvariantCulture), 0);
                    }
                    break;
            }
        }

        /// <summary>
        /// Prepares one CPU NV12 frame: Y plane (<paramref name="stride"/> × height) immediately
        /// followed by the interleaved UV plane (<paramref name="stride"/> × height/2).
        /// </summary>
        public void PrepareNv12(byte* data, int stride, long ptsGridIndex)
        {
            if (_frame == null)
                throw new InvalidOperationException("This encoder takes GPU input.");

            ffmpeg.av_frame_make_writable(_frame);

            byte* uv = data + (long)stride * _height;
            if (_sws == null)
            {
                CopyPlane(data, stride, _frame->data[0], _frame->linesize[0], _width, _height);
                CopyPlane(uv, stride, _frame->data[1], _frame->linesize[1], _width, _height / 2);
            }
            else
            {
                var srcData = new byte_ptrArray8();
                var srcStride = new int_array8();
                srcData[0] = data;
                srcData[1] = uv;
                srcStride[0] = stride;
                srcStride[1] = stride;
                ffmpeg.sws_scale(_sws, srcData, srcStride, 0, _height, _frame->data, _frame->linesize);
            }

            SetPrepared(_frame, ptsGridIndex);
        }

        /// <summary>
        /// Prepares the GPU frame in ring slot <paramref name="slot"/>. Returns false when the
        /// slot could not be read in time (nothing is prepared; skip the frame).
        /// </summary>
        public bool PrepareShared(int slot, long ptsGridIndex)
        {
            if (_gpuInput == null)
                throw new InvalidOperationException("This encoder takes CPU input.");

            ffmpeg.av_frame_unref(_hwFrame);
            if (!_gpuInput.TryFillFrame(slot, _hwFrame))
            {
                _hasPicture = false;
                return false;
            }

            SetPrepared(_hwFrame, ptsGridIndex);
            return true;
        }

        /// <summary>
        /// Prepares a repeat of the most recently prepared picture with a new PTS (no pixel
        /// work at all). Returns false when there is no picture yet.
        /// </summary>
        public bool PrepareRepeat(long ptsGridIndex)
        {
            if (!_hasPicture)
                return false;

            SetPrepared(_gpuInput != null ? _hwFrame : _frame, ptsGridIndex);
            return true;
        }

        private void SetPrepared(AVFrame* frame, long pts)
        {
            frame->pts = pts;

            // Keyframes by elapsed grid time (≥ 2 s), which stays right when frames are sparse.
            if (_lastKeyPts == long.MinValue || pts - _lastKeyPts >= _keyIntervalTicks)
            {
                frame->pict_type = AVPictureType.AV_PICTURE_TYPE_I;
                _lastKeyPts = pts;
            }
            else
            {
                frame->pict_type = AVPictureType.AV_PICTURE_TYPE_NONE;
            }

            _prepared = frame;
            _hasPicture = true;
        }

        /// <summary>
        /// Submits the prepared frame. Returns false when the encoder's input is full (EAGAIN):
        /// drain <see cref="TryReceive"/> and call again.
        /// </summary>
        public bool TrySendPrepared()
        {
            if (_prepared == null)
                return true;

            int result = ffmpeg.avcodec_send_frame(_ctx, _prepared);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                return false;
            if (result < 0)
                throw new InvalidOperationException($"avcodec_send_frame (video) failed: {FFmpegHelper.GetErrorString(result)}");

            _prepared = null;
            return true;
        }

        private static void CopyPlane(byte* src, int srcStride, byte* dst, int dstStride, int widthBytes, int rows)
        {
            if (srcStride == dstStride)
            {
                Buffer.MemoryCopy(src, dst, (long)dstStride * rows, (long)srcStride * (rows - 1) + widthBytes);
                return;
            }

            int bytes = Math.Min(Math.Min(srcStride, dstStride), widthBytes);
            for (int y = 0; y < rows; y++)
                Buffer.MemoryCopy(src + (long)y * srcStride, dst + (long)y * dstStride, dstStride, bytes);
        }

        /// <summary>
        /// Pulls the next encoded packet. The packet's <c>Data</c> points into encoder-owned
        /// memory that stays valid only until the next TryReceive/Dispose — copy it out first.
        /// Returns false when the encoder needs more input or is fully drained.
        /// </summary>
        public bool TryReceive(out EncodedVideoPacket packet)
        {
            ffmpeg.av_packet_unref(_packet);

            int result = ffmpeg.avcodec_receive_packet(_ctx, _packet);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
            {
                packet = default;
                return false;
            }

            if (result < 0)
                throw new InvalidOperationException($"avcodec_receive_packet (video) failed: {FFmpegHelper.GetErrorString(result)}");

            bool isKey = (_packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
            if (isKey && _packet->pts > _lastKeyPts)
                _lastKeyPts = _packet->pts; // the encoder inserted one on its own (scene cut / GOP)

            packet = new EncodedVideoPacket(_packet->pts, _packet->dts, (nint)_packet->data, _packet->size, isKey);
            return true;
        }

        /// <summary>Signals end-of-stream so buffered frames can be drained via TryReceive.</summary>
        public void FlushSend()
        {
            _prepared = null;
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

            if (_hwFrame != null)
            {
                fixed (AVFrame** frame = &_hwFrame)
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

            // After the codec context and frames: they hold references into the frame pool.
            _gpuInput?.Dispose();
            _gpuInput = null;

            if (_headerData != 0)
            {
                Marshal.FreeHGlobal(_headerData);
                _headerData = 0;
            }
        }
    }
}
