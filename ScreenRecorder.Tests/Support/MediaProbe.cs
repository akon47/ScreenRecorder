using System;
using System.Collections.Generic;
using System.Linq;
using FFmpeg.AutoGen;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ScreenRecorder.Tests.Support
{
    /// <summary>One decoded picture as tightly packed 4:2:0 planes.</summary>
    internal sealed class DecodedVideoFrame
    {
        public int Width;
        public int Height;
        public double PtsSeconds;
        public byte[] Y;
        public byte[] U;
        public byte[] V;

        public int ChromaWidth => (Width + 1) / 2;

        public byte LumaAt(int x, int y) => Y[y * Width + x];

        public byte UAt(int chromaX, int chromaY) => U[chromaY * ChromaWidth + chromaX];

        public byte VAt(int chromaX, int chromaY) => V[chromaY * ChromaWidth + chromaX];

        public double MeanLuma() => Y.Average(b => (double)b);
    }

    /// <summary>
    /// In-process inspection of a produced media file through libavformat/libavcodec (no
    /// external ffprobe): stream metadata, every packet's timestamp and keyframe flag, and
    /// optional decoding. Extended from the encoder harness's MediaFileVerifier.
    /// </summary>
    internal sealed unsafe class MediaProbe
    {
        /// <summary>False when libavformat could not open the file at all.</summary>
        public bool Opened;

        public int StreamCount;
        public bool HasVideo;
        public bool HasAudio;
        public string VideoCodec = "(none)";
        public string AudioCodec = "(none)";
        public int Width;
        public int Height;
        public int SampleRate;
        public int Channels;

        /// <summary>Container duration in seconds (0 when unknown).</summary>
        public double DurationSeconds;

        /// <summary>The video stream's average frame rate, or 0 when unset.</summary>
        public double AverageFrameRate;

        /// <summary>Presentation time of every video packet, in file order, in seconds.</summary>
        public readonly List<double> VideoPts = new List<double>();

        public readonly List<double> VideoDts = new List<double>();

        public readonly List<bool> VideoKeyFrames = new List<bool>();

        /// <summary>Presentation time of every audio packet, in file order, in seconds.</summary>
        public readonly List<double> AudioPts = new List<double>();

        public int VideoPacketCount => VideoPts.Count;

        public int AudioPacketCount => AudioPts.Count;

        public static MediaProbe Probe(string path)
        {
            var info = new MediaProbe();

            AVFormatContext* fmt = null;
            if (ffmpeg.avformat_open_input(&fmt, path, null, null) < 0)
                return info;

            info.Opened = true;
            try
            {
                ffmpeg.avformat_find_stream_info(fmt, null);

                info.StreamCount = (int)fmt->nb_streams;
                info.DurationSeconds = fmt->duration > 0 ? fmt->duration / (double)ffmpeg.AV_TIME_BASE : 0;

                int videoStream = -1;
                int audioStream = -1;
                for (int i = 0; i < fmt->nb_streams; i++)
                {
                    AVStream* stream = fmt->streams[i];
                    AVCodecParameters* par = stream->codecpar;
                    if (par->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO && videoStream < 0)
                    {
                        videoStream = i;
                        info.HasVideo = true;
                        info.VideoCodec = ffmpeg.avcodec_get_name(par->codec_id);
                        info.Width = par->width;
                        info.Height = par->height;
                        if (stream->avg_frame_rate.num > 0 && stream->avg_frame_rate.den > 0)
                            info.AverageFrameRate = stream->avg_frame_rate.num / (double)stream->avg_frame_rate.den;
                    }
                    else if (par->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO && audioStream < 0)
                    {
                        audioStream = i;
                        info.HasAudio = true;
                        info.AudioCodec = ffmpeg.avcodec_get_name(par->codec_id);
                        info.SampleRate = par->sample_rate;
                        info.Channels = par->ch_layout.nb_channels;
                    }
                }

                AVPacket* packet = ffmpeg.av_packet_alloc();
                try
                {
                    while (ffmpeg.av_read_frame(fmt, packet) >= 0)
                    {
                        int idx = packet->stream_index;
                        AVRational tb = fmt->streams[idx]->time_base;
                        double pts = ToSeconds(packet->pts, tb);
                        double dts = ToSeconds(packet->dts, tb);

                        if (idx == videoStream)
                        {
                            // Containers without presentation times (AVI) only carry DTS.
                            info.VideoPts.Add(double.IsNaN(pts) ? dts : pts);
                            info.VideoDts.Add(dts);
                            info.VideoKeyFrames.Add((packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0);
                        }
                        else if (idx == audioStream)
                        {
                            info.AudioPts.Add(double.IsNaN(pts) ? dts : pts);
                        }

                        ffmpeg.av_packet_unref(packet);
                    }
                }
                finally
                {
                    ffmpeg.av_packet_free(&packet);
                }
            }
            finally
            {
                ffmpeg.avformat_close_input(&fmt);
            }

            return info;
        }

        private static double ToSeconds(long timestamp, AVRational timeBase)
        {
            return timestamp == ffmpeg.AV_NOPTS_VALUE
                ? double.NaN
                : timestamp * (double)timeBase.num / timeBase.den;
        }

        /// <summary>Decodes up to <paramref name="maxFrames"/> pictures of the first video stream.</summary>
        public static List<DecodedVideoFrame> DecodeVideo(string path, int maxFrames = int.MaxValue)
        {
            var frames = new List<DecodedVideoFrame>();

            AVFormatContext* fmt = null;
            if (ffmpeg.avformat_open_input(&fmt, path, null, null) < 0)
                Assert.Fail($"Could not open '{path}' for decoding.");

            try
            {
                ffmpeg.avformat_find_stream_info(fmt, null);

                int vIndex = -1;
                for (int i = 0; i < fmt->nb_streams; i++)
                {
                    if (fmt->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                    {
                        vIndex = i;
                        break;
                    }
                }

                if (vIndex < 0)
                    return frames;

                AVStream* stream = fmt->streams[vIndex];
                AVCodec* decoder = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
                if (decoder == null)
                    Assert.Fail("No decoder for the recorded video codec.");

                AVCodecContext* ctx = ffmpeg.avcodec_alloc_context3(decoder);
                AVFrame* frame = ffmpeg.av_frame_alloc();
                AVPacket* packet = ffmpeg.av_packet_alloc();
                try
                {
                    ffmpeg.avcodec_parameters_to_context(ctx, stream->codecpar);
                    ctx->thread_count = 1;
                    if (ffmpeg.avcodec_open2(ctx, decoder, null) < 0)
                        Assert.Fail("Could not open the video decoder.");

                    bool endOfFile = false;
                    while (!endOfFile && frames.Count < maxFrames)
                    {
                        if (ffmpeg.av_read_frame(fmt, packet) < 0)
                        {
                            ffmpeg.avcodec_send_packet(ctx, null); // drain
                            endOfFile = true;
                        }
                        else
                        {
                            if (packet->stream_index == vIndex)
                                ffmpeg.avcodec_send_packet(ctx, packet);
                            ffmpeg.av_packet_unref(packet);
                        }

                        while (frames.Count < maxFrames && ffmpeg.avcodec_receive_frame(ctx, frame) >= 0)
                        {
                            frames.Add(CopyFrame(frame, stream->time_base));
                            ffmpeg.av_frame_unref(frame);
                        }
                    }
                }
                finally
                {
                    ffmpeg.av_packet_free(&packet);
                    ffmpeg.av_frame_free(&frame);
                    ffmpeg.avcodec_free_context(&ctx);
                }
            }
            finally
            {
                ffmpeg.avformat_close_input(&fmt);
            }

            return frames;
        }

        private static DecodedVideoFrame CopyFrame(AVFrame* frame, AVRational timeBase)
        {
            var format = (AVPixelFormat)frame->format;
            int width = frame->width;
            int height = frame->height;
            int chromaWidth = (width + 1) / 2;
            int chromaHeight = (height + 1) / 2;

            var result = new DecodedVideoFrame
            {
                Width = width,
                Height = height,
                PtsSeconds = ToSeconds(frame->best_effort_timestamp, timeBase),
                Y = new byte[width * height],
                U = new byte[chromaWidth * chromaHeight],
                V = new byte[chromaWidth * chromaHeight],
            };

            for (int row = 0; row < height; row++)
                new ReadOnlySpan<byte>(frame->data[0] + (long)row * frame->linesize[0], width).CopyTo(result.Y.AsSpan(row * width));

            if (format == AVPixelFormat.AV_PIX_FMT_YUV420P || format == AVPixelFormat.AV_PIX_FMT_YUVJ420P)
            {
                for (int row = 0; row < chromaHeight; row++)
                {
                    new ReadOnlySpan<byte>(frame->data[1] + (long)row * frame->linesize[1], chromaWidth).CopyTo(result.U.AsSpan(row * chromaWidth));
                    new ReadOnlySpan<byte>(frame->data[2] + (long)row * frame->linesize[2], chromaWidth).CopyTo(result.V.AsSpan(row * chromaWidth));
                }
            }
            else if (format == AVPixelFormat.AV_PIX_FMT_NV12)
            {
                for (int row = 0; row < chromaHeight; row++)
                {
                    byte* line = frame->data[1] + (long)row * frame->linesize[1];
                    for (int x = 0; x < chromaWidth; x++)
                    {
                        result.U[row * chromaWidth + x] = line[x * 2];
                        result.V[row * chromaWidth + x] = line[x * 2 + 1];
                    }
                }
            }
            else
            {
                Assert.Fail($"Unexpected decoded pixel format {format}.");
            }

            return result;
        }

        /// <summary>
        /// Presentation time (seconds) of the first decoded audio sample whose magnitude exceeds
        /// <paramref name="threshold"/>, or NaN when the audio never gets that loud.
        /// </summary>
        public static double FindAudioOnset(string path, double threshold)
        {
            double onset = double.NaN;
            DecodeAudio(path, (samples, channels, planar, frame, timeSeconds, sampleRate) =>
            {
                if (!double.IsNaN(onset))
                    return;

                for (int s = 0; s < samples; s++)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        float v = planar ? ((float*)frame->data[(uint)ch])[s] : ((float*)frame->data[0])[s * channels + ch];
                        if (Math.Abs(v) > threshold)
                        {
                            onset = timeSeconds + s / (double)sampleRate;
                            return;
                        }
                    }
                }
            });
            return onset;
        }

        /// <summary>Decodes the audio stream and returns (rms, peak) over all samples, full scale = 1.</summary>
        public static (double Rms, double Peak) MeasureAudioLevel(string path)
        {
            double sumSquares = 0, peak = 0;
            long count = 0;
            DecodeAudio(path, (samples, channels, planar, frame, timeSeconds, sampleRate) =>
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    float* data = planar ? (float*)frame->data[(uint)ch] : (float*)frame->data[0];
                    for (int s = 0; s < samples; s++)
                    {
                        float v = planar ? data[s] : data[s * channels + ch];
                        double a = Math.Abs(v);
                        if (a > peak) peak = a;
                        sumSquares += (double)v * v;
                        count++;
                    }
                }
            });

            return (count > 0 ? Math.Sqrt(sumSquares / count) : 0, peak);
        }

        private delegate void AudioFrameHandler(int samples, int channels, bool planar, AVFrame* frame, double timeSeconds, int sampleRate);

        /// <summary>
        /// Decodes the first audio stream (float output) and hands every frame to the handler
        /// together with its presentation time; frames without a timestamp continue from the
        /// running sample count.
        /// </summary>
        private static void DecodeAudio(string path, AudioFrameHandler handler)
        {
            AVFormatContext* fmt = null;
            if (ffmpeg.avformat_open_input(&fmt, path, null, null) < 0)
                Assert.Fail($"Could not open '{path}' for decoding.");

            long decodedSamples = 0;
            try
            {
                ffmpeg.avformat_find_stream_info(fmt, null);

                int aIndex = -1;
                for (int i = 0; i < fmt->nb_streams; i++)
                {
                    if (fmt->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                    {
                        aIndex = i;
                        break;
                    }
                }

                if (aIndex < 0)
                    return;

                AVRational timeBase = fmt->streams[aIndex]->time_base;
                AVCodecParameters* par = fmt->streams[aIndex]->codecpar;
                AVCodec* decoder = ffmpeg.avcodec_find_decoder(par->codec_id);
                if (decoder == null)
                    Assert.Fail("No decoder for the recorded audio codec.");

                AVCodecContext* ctx = ffmpeg.avcodec_alloc_context3(decoder);
                AVFrame* frame = ffmpeg.av_frame_alloc();
                AVPacket* packet = ffmpeg.av_packet_alloc();
                try
                {
                    ffmpeg.avcodec_parameters_to_context(ctx, par);
                    ctx->pkt_timebase = timeBase;
                    if (ffmpeg.avcodec_open2(ctx, decoder, null) < 0)
                        Assert.Fail("Could not open the audio decoder.");

                    bool endOfFile = false;
                    while (!endOfFile)
                    {
                        if (ffmpeg.av_read_frame(fmt, packet) < 0)
                        {
                            ffmpeg.avcodec_send_packet(ctx, null);
                            endOfFile = true;
                        }
                        else
                        {
                            if (packet->stream_index == aIndex)
                                ffmpeg.avcodec_send_packet(ctx, packet);
                            ffmpeg.av_packet_unref(packet);
                        }

                        while (ffmpeg.avcodec_receive_frame(ctx, frame) >= 0)
                        {
                            var sampleFormat = (AVSampleFormat)frame->format;
                            if (sampleFormat != AVSampleFormat.AV_SAMPLE_FMT_FLTP && sampleFormat != AVSampleFormat.AV_SAMPLE_FMT_FLT)
                                Assert.Fail($"Unexpected decoded sample format {sampleFormat}.");

                            bool planar = sampleFormat == AVSampleFormat.AV_SAMPLE_FMT_FLTP;
                            int channels = frame->ch_layout.nb_channels;
                            int samples = frame->nb_samples;
                            int sampleRate = frame->sample_rate > 0 ? frame->sample_rate : par->sample_rate;
                            double time = ToSeconds(frame->best_effort_timestamp, timeBase);
                            if (double.IsNaN(time))
                                time = decodedSamples / (double)sampleRate;

                            handler(samples, channels, planar, frame, time, sampleRate);
                            decodedSamples += samples;

                            ffmpeg.av_frame_unref(frame);
                        }
                    }
                }
                finally
                {
                    ffmpeg.av_packet_free(&packet);
                    ffmpeg.av_frame_free(&frame);
                    ffmpeg.avcodec_free_context(&ctx);
                }
            }
            finally
            {
                ffmpeg.avformat_close_input(&fmt);
            }
        }
    }
}
