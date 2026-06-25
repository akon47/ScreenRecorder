using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace MediaEncoder
{
    /// <summary>
    /// FFmpeg enum maps, error-string formatting, and the hardware-encoder availability probe.
    /// </summary>
    internal static unsafe class FFmpegHelper
    {
        public static string GetErrorString(int error)
        {
            const int bufferSize = 1024;
            byte* buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, bufferSize);
            return Marshal.PtrToStringAnsi((IntPtr)buffer);
        }

        public static AVCodecID ToAVCodecID(VideoCodec codec)
        {
            switch (codec)
            {
                case VideoCodec.H264: return AVCodecID.AV_CODEC_ID_H264;
                case VideoCodec.Hevc: return AVCodecID.AV_CODEC_ID_HEVC; // == H265
                default: throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported video codec");
            }
        }

        public static AVCodecID ToAVCodecID(AudioCodec codec)
        {
            switch (codec)
            {
                case AudioCodec.Aac: return AVCodecID.AV_CODEC_ID_AAC;
                case AudioCodec.Mp3: return AVCodecID.AV_CODEC_ID_MP3;
                default: throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported audio codec");
            }
        }

        public static AVPixelFormat ToAVPixelFormat(PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.NV12: return AVPixelFormat.AV_PIX_FMT_NV12;
                case PixelFormat.YUV420P: return AVPixelFormat.AV_PIX_FMT_YUV420P;
                case PixelFormat.BGRA: return AVPixelFormat.AV_PIX_FMT_BGRA;
                case PixelFormat.BGR24: return AVPixelFormat.AV_PIX_FMT_BGR24;
                case PixelFormat.RGB24: return AVPixelFormat.AV_PIX_FMT_RGB24;
                default: return AVPixelFormat.AV_PIX_FMT_NONE;
            }
        }

        public static PixelFormat ToPixelFormat(AVPixelFormat format)
        {
            switch (format)
            {
                case AVPixelFormat.AV_PIX_FMT_NV12: return PixelFormat.NV12;
                case AVPixelFormat.AV_PIX_FMT_YUV420P: return PixelFormat.YUV420P;
                case AVPixelFormat.AV_PIX_FMT_BGRA: return PixelFormat.BGRA;
                case AVPixelFormat.AV_PIX_FMT_BGR24: return PixelFormat.BGR24;
                case AVPixelFormat.AV_PIX_FMT_RGB24: return PixelFormat.RGB24;
                default: return PixelFormat.None;
            }
        }

        public static AVSampleFormat ToAVSampleFormat(SampleFormat format)
        {
            switch (format)
            {
                case SampleFormat.U8: return AVSampleFormat.AV_SAMPLE_FMT_U8;
                case SampleFormat.S16: return AVSampleFormat.AV_SAMPLE_FMT_S16;
                case SampleFormat.S32: return AVSampleFormat.AV_SAMPLE_FMT_S32;
                case SampleFormat.FLT: return AVSampleFormat.AV_SAMPLE_FMT_FLT;
                case SampleFormat.FLTP: return AVSampleFormat.AV_SAMPLE_FMT_FLTP;
                default: return AVSampleFormat.AV_SAMPLE_FMT_NONE;
            }
        }

        /// <summary>True if any of the named encoders can actually be opened at 1080p60 here.</summary>
        public static bool IsEncoderUsable(params string[] names)
        {
            foreach (var name in names)
            {
                if (IsEncoderUsable(name))
                    return true;
            }

            return false;
        }

        public static bool IsEncoderUsable(string name)
        {
            FFmpegBootstrap.EnsureInitialized();

            AVCodec* codec = ffmpeg.avcodec_find_encoder_by_name(name);
            if (codec == null)
                return false;

            AVCodecContext* ctx = ffmpeg.avcodec_alloc_context3(codec);
            if (ctx == null)
                return false;

            try
            {
                ctx->width = 1920;
                ctx->height = 1080;
                ctx->time_base = new AVRational { num = 1, den = 60 };
                ctx->framerate = new AVRational { num = 60, den = 1 };

                AVPixelFormat* pixFmts = null;
                int count = 0;
                int gr = ffmpeg.avcodec_get_supported_config(
                    ctx, codec, AVCodecConfig.AV_CODEC_CONFIG_PIX_FORMAT, 0, (void**)&pixFmts, &count);
                ctx->pix_fmt = (gr >= 0 && pixFmts != null && count > 0)
                    ? pixFmts[0]
                    : AVPixelFormat.AV_PIX_FMT_YUV420P;

                return ffmpeg.avcodec_open2(ctx, codec, null) >= 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                ffmpeg.avcodec_free_context(&ctx);
            }
        }
    }
}
