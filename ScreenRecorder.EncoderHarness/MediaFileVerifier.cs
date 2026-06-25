using System;
using System.Collections.Generic;
using FFmpeg.AutoGen;

namespace ScreenRecorder.EncoderHarness
{
    /// <summary>
    /// In-process verification of a produced media file via libavformat (no external ffprobe).
    /// Opens the file, reads stream metadata, counts packets per stream, and records the first
    /// PTS per stream so the harness can assert codec/dimensions/CFR-frame-count/duration/sync.
    /// </summary>
    internal sealed unsafe class MediaFileVerifier
    {
        public bool Ok { get; private set; } = true;
        public int StreamCount;
        public string VideoCodec = "(none)";
        public int Width;
        public int Height;
        public string AudioCodec = "(none)";
        public int SampleRate;
        public int Channels;
        public long VideoPacketCount;
        public long AudioPacketCount;
        public double DurationSeconds;
        public double FirstVideoPtsSeconds = double.NaN;
        public double FirstAudioPtsSeconds = double.NaN;

        private readonly List<string> _failures = new List<string>();

        public IReadOnlyList<string> Failures => _failures;

        public void Check(bool condition, string description)
        {
            Console.WriteLine((condition ? "  [PASS] " : "  [FAIL] ") + description);
            if (!condition)
            {
                Ok = false;
                _failures.Add(description);
            }
        }

        public static MediaFileVerifier Probe(string path)
        {
            var info = new MediaFileVerifier();

            AVFormatContext* fmt = null;
            int openResult = ffmpeg.avformat_open_input(&fmt, path, null, null);
            if (openResult < 0)
            {
                info.Ok = false;
                info._failures.Add("avformat_open_input failed");
                return info;
            }

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
                        info.VideoCodec = GetCodecName(par->codec_id);
                        info.Width = par->width;
                        info.Height = par->height;
                    }
                    else if (par->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO && audioStream < 0)
                    {
                        audioStream = i;
                        info.AudioCodec = GetCodecName(par->codec_id);
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
                        AVStream* stream = fmt->streams[idx];
                        double ptsSeconds = packet->pts == ffmpeg.AV_NOPTS_VALUE
                            ? double.NaN
                            : packet->pts * stream->time_base.num / (double)stream->time_base.den;

                        if (idx == videoStream)
                        {
                            info.VideoPacketCount++;
                            if (double.IsNaN(info.FirstVideoPtsSeconds))
                                info.FirstVideoPtsSeconds = ptsSeconds;
                        }
                        else if (idx == audioStream)
                        {
                            info.AudioPacketCount++;
                            if (double.IsNaN(info.FirstAudioPtsSeconds))
                                info.FirstAudioPtsSeconds = ptsSeconds;
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

        private static string GetCodecName(AVCodecID id)
        {
            return ffmpeg.avcodec_get_name(id) ?? id.ToString();
        }

        /// <summary>Decodes the audio stream and returns (rms, peak) over all samples in [0,1].</summary>
        public static (double Rms, double Peak) MeasureAudioRms(string path)
        {
            AVFormatContext* fmt = null;
            if (ffmpeg.avformat_open_input(&fmt, path, null, null) < 0)
                return (0, 0);

            double sumSq = 0, peak = 0;
            long n = 0;
            try
            {
                ffmpeg.avformat_find_stream_info(fmt, null);
                int aIndex = -1;
                for (int i = 0; i < fmt->nb_streams; i++)
                    if (fmt->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO) { aIndex = i; break; }
                if (aIndex < 0)
                    return (0, 0);

                AVCodecParameters* par = fmt->streams[aIndex]->codecpar;
                AVCodec* dec = ffmpeg.avcodec_find_decoder(par->codec_id);
                AVCodecContext* dctx = ffmpeg.avcodec_alloc_context3(dec);
                ffmpeg.avcodec_parameters_to_context(dctx, par);
                ffmpeg.avcodec_open2(dctx, dec, null);

                AVFrame* frame = ffmpeg.av_frame_alloc();
                AVPacket* packet = ffmpeg.av_packet_alloc();
                int channels = par->ch_layout.nb_channels;

                while (ffmpeg.av_read_frame(fmt, packet) >= 0)
                {
                    if (packet->stream_index == aIndex && ffmpeg.avcodec_send_packet(dctx, packet) >= 0)
                    {
                        while (ffmpeg.avcodec_receive_frame(dctx, frame) >= 0)
                        {
                            var sampleFmt = (AVSampleFormat)frame->format;
                            bool planar = ffmpeg.av_sample_fmt_is_planar(sampleFmt) != 0;
                            int samples = frame->nb_samples;
                            for (int ch = 0; ch < channels; ch++)
                            {
                                float* data = planar ? (float*)frame->data[(uint)ch] : (float*)frame->data[0];
                                for (int s = 0; s < samples; s++)
                                {
                                    float v = planar ? data[s] : data[s * channels + ch];
                                    double a = Math.Abs(v);
                                    if (a > peak) peak = a;
                                    sumSq += (double)v * v;
                                    n++;
                                }
                            }
                        }
                    }
                    ffmpeg.av_packet_unref(packet);
                }

                ffmpeg.av_frame_free(&frame);
                ffmpeg.av_packet_free(&packet);
                ffmpeg.avcodec_free_context(&dctx);
            }
            finally
            {
                ffmpeg.avformat_close_input(&fmt);
            }

            double rms = n > 0 ? Math.Sqrt(sumSq / n) : 0;
            return (rms, peak);
        }
    }
}
