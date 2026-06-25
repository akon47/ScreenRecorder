using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using FFmpeg.AutoGen;
using MediaEncoder;
using ScreenRecorder.DirectX;
using ScreenRecorder.Encoder;
using ScreenRecorder.VideoSource;
using Rect = System.Windows.Rect;

namespace ScreenRecorder.EncoderHarness
{
    /// <summary>
    /// Captures the real primary monitor for a few seconds via the WGC + Vortice + paced-clock
    /// pipeline, produces an mp4, and verifies it (dimensions, frame count, frames-not-blank via
    /// luma variance), extracting one decoded frame to PNG for visual inspection.
    /// </summary>
    internal static unsafe class ScreenCaptureSmoke
    {
        public static int Run(int seconds, int fps, string output, string pngPath, bool cursor, Func<VideoCodec, string, HwAccel> resolveHw)
        {
            if (!Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported())
            {
                Console.WriteLine("WGC not supported on this OS.");
                return 2;
            }

            var mon = MonitorInfo.GetPrimaryMonitorInfo();
            int w = mon.Width & ~1;
            int h = mon.Height & ~1;
            HwAccel hw = resolveHw(VideoCodec.H264, "auto");

            Console.WriteLine($"=== Screen capture: {seconds}s {w}x{h}@{fps} H264/{hw} (monitor {mon.DeviceName}) ===");
            Console.WriteLine("Output: " + output);

            var videoParams = new VideoParams
            {
                Codec = VideoCodec.H264,
                Hw = hw,
                Width = w,
                Height = h,
                FpsNumerator = fps,
                FpsDenominator = 1,
                Bitrate = 20_000_000,
                RateControl = RateControl.Cbr,
            };

            long t0 = Stopwatch.GetTimestamp();
            using (var recorder = new Recorder(output, "mp4", videoParams, null))
            {
                recorder.Start();
                using var src = new ScreenVideoSource(mon.DeviceName, new Rect(0, 0, w, h), cursor, fps, 1, t0, recorder);
                src.Start();
                Thread.Sleep(seconds * 1000);
                src.Stop();
                recorder.Stop();
                Console.WriteLine($"Dropped frames: {src.DroppedFrames}");
            }

            Console.WriteLine();
            Console.WriteLine("=== Verification ===");
            var info = MediaFileVerifier.Probe(output);
            int expected = fps * seconds;
            Console.WriteLine($"  video={info.VideoCodec} {info.Width}x{info.Height} vPackets={info.VideoPacketCount} dur={info.DurationSeconds:0.000}s");
            info.Check(info.VideoCodec == "h264", "video codec h264");
            info.Check(info.Width == w && info.Height == h, $"dimensions {w}x{h}");
            info.Check(Math.Abs(info.VideoPacketCount - expected) <= fps, $"CFR-ish ~{expected} frames (got {info.VideoPacketCount})");
            info.Check(Math.Abs(info.DurationSeconds - seconds) < 0.6, $"duration ~= {seconds}s");

            double variance = AnalyzeAndSavePng(output, (int)(info.VideoPacketCount / 2), pngPath);
            info.Check(variance > 5.0, $"luma variance {variance:0.0} > 5 (frames not blank)");
            Console.WriteLine("  PNG: " + pngPath);

            Console.WriteLine();
            Console.WriteLine(info.Ok ? "RESULT: ALL CHECKS PASSED" : "RESULT: FAILED");
            return info.Ok ? 0 : 1;
        }

        private static double AnalyzeAndSavePng(string path, int targetFrame, string pngPath)
        {
            AVFormatContext* fmt = null;
            if (ffmpeg.avformat_open_input(&fmt, path, null, null) < 0)
                return 0;

            double variance = 0;
            try
            {
                ffmpeg.avformat_find_stream_info(fmt, null);
                int vIndex = -1;
                for (int i = 0; i < fmt->nb_streams; i++)
                    if (fmt->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO) { vIndex = i; break; }
                if (vIndex < 0)
                    return 0;

                AVCodecParameters* par = fmt->streams[vIndex]->codecpar;
                AVCodec* dec = ffmpeg.avcodec_find_decoder(par->codec_id);
                AVCodecContext* dctx = ffmpeg.avcodec_alloc_context3(dec);
                ffmpeg.avcodec_parameters_to_context(dctx, par);
                ffmpeg.avcodec_open2(dctx, dec, null);

                AVFrame* frame = ffmpeg.av_frame_alloc();
                AVPacket* packet = ffmpeg.av_packet_alloc();
                int decoded = 0;
                bool done = false;

                while (!done && ffmpeg.av_read_frame(fmt, packet) >= 0)
                {
                    if (packet->stream_index == vIndex && ffmpeg.avcodec_send_packet(dctx, packet) >= 0)
                    {
                        while (ffmpeg.avcodec_receive_frame(dctx, frame) >= 0)
                        {
                            decoded++;
                            if (decoded >= Math.Max(1, targetFrame))
                            {
                                variance = LumaVariance(frame, dctx->width, dctx->height);
                                TrySavePng(frame, dctx, pngPath);
                                done = true;
                                break;
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

            return variance;
        }

        private static double LumaVariance(AVFrame* frame, int width, int height)
        {
            byte* y = frame->data[0];
            int stride = frame->linesize[0];
            double sum = 0, sumSq = 0;
            long n = (long)width * height;
            for (int row = 0; row < height; row++)
            {
                byte* line = y + (long)row * stride;
                for (int col = 0; col < width; col++)
                {
                    int v = line[col];
                    sum += v;
                    sumSq += (double)v * v;
                }
            }
            double mean = sum / n;
            return sumSq / n - mean * mean;
        }

        private static void TrySavePng(AVFrame* frame, AVCodecContext* dctx, string pngPath)
        {
            try
            {
                int w = dctx->width, h = dctx->height;
                SwsContext* sws = ffmpeg.sws_getContext(w, h, dctx->pix_fmt, w, h, AVPixelFormat.AV_PIX_FMT_BGRA, 2, null, null, null);
                if (sws == null) return;

                int dstStride = w * 4;
                byte[] buffer = new byte[dstStride * h];
                fixed (byte* dst = buffer)
                {
                    var dstData = new byte_ptrArray4(); dstData[0] = dst;
                    var dstLine = new int_array4(); dstLine[0] = dstStride;
                    ffmpeg.sws_scale(sws, frame->data, frame->linesize, 0, h, dstData, dstLine);
                }
                ffmpeg.sws_freeContext(sws);

                using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                System.Runtime.InteropServices.Marshal.Copy(buffer, 0, bd.Scan0, buffer.Length);
                bmp.UnlockBits(bd);
                bmp.Save(pngPath, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  (PNG save failed: " + ex.Message + ")");
            }
        }
    }
}
