using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MediaEncoder;
using ScreenRecorder.AudioSource;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.EncoderHarness
{
    /// <summary>
    /// Captures real system loopback (+ optional mic) into an mp4 alongside a synthetic NV12 video
    /// pattern, then verifies the audio stream (codec/rate/channels, duration, A/V start aligned)
    /// and measures audio RMS/peak. Silence is a WARN (not a fail) so a headless/CI box with no
    /// audio playing still passes — non-silence is the local "real audio" proof.
    /// </summary>
    internal static unsafe class AudioCaptureSmoke
    {
        public static int Run(int seconds, string output, bool useMic, HwAccel hw)
        {
            const int fps = 30, w = 1280, h = 720;
            Console.WriteLine($"=== Audio capture: {seconds}s, mic={useMic}, video=synthetic {w}x{h}@{fps} ===");
            Console.WriteLine("HINT: play music/audio on the default output device now — loopback records whatever is playing.");
            Console.WriteLine("Output: " + output);

            var videoParams = new VideoParams
            {
                Codec = VideoCodec.H264, Hw = hw, Width = w, Height = h,
                FpsNumerator = fps, FpsDenominator = 1, Bitrate = 8_000_000, RateControl = RateControl.Cbr,
            };
            var audioParams = new AudioParams
            {
                Codec = AudioCodec.Aac, SampleRate = 48000, Channels = 2, Bitrate = 160_000, Format = SampleFormat.S16,
            };

            long t0 = Stopwatch.GetTimestamp();
            long freq = Stopwatch.Frequency;
            bool loopbackActive;

            using (var recorder = new Recorder(output, "mp4", videoParams, audioParams))
            {
                recorder.Start();

                using var audio = new WasapiAudioSource(loopbackDeviceId: null, micDeviceId: useMic ? "" : null, t0, recorder);
                audio.Start();
                loopbackActive = audio.IsLoopbackActive;

                var videoTask = Task.Run(() => GenerateSyntheticVideo(recorder, videoParams, fps * seconds, t0, freq));
                Thread.Sleep(seconds * 1000);

                videoTask.Wait();
                audio.Stop();
                recorder.Stop();
                Console.WriteLine($"Loopback active: {loopbackActive}");
            }

            Console.WriteLine();
            Console.WriteLine("=== Verification ===");
            var info = MediaFileVerifier.Probe(output);
            Console.WriteLine($"  streams={info.StreamCount} video={info.VideoCodec} audio={info.AudioCodec} {info.SampleRate}Hz/{info.Channels}ch " +
                              $"vPackets={info.VideoPacketCount} aPackets={info.AudioPacketCount} dur={info.DurationSeconds:0.000}s " +
                              $"firstV={info.FirstVideoPtsSeconds:0.000} firstA={info.FirstAudioPtsSeconds:0.000}");

            info.Check(info.StreamCount == 2, "stream count == 2 (video + audio)");
            info.Check(info.AudioCodec == "aac", "audio codec aac");
            info.Check(info.SampleRate == 48000, "audio sample rate 48000");
            info.Check(info.Channels == 2, "audio channels 2");
            info.Check(Math.Abs(info.DurationSeconds - seconds) < 0.6, $"duration ~= {seconds}s");
            info.Check(!double.IsNaN(info.FirstAudioPtsSeconds) && Math.Abs(info.FirstVideoPtsSeconds - info.FirstAudioPtsSeconds) < 0.06, "A/V start aligned (<60ms)");

            var (rms, peak) = MediaFileVerifier.MeasureAudioRms(output);
            Console.WriteLine($"  audio rms={rms:0.0000} peak={peak:0.0000}");
            if (peak > 0.01 && rms > 0.001)
                Console.WriteLine("  [PASS] audio non-silent");
            else
                Console.WriteLine($"  [WARN] audio is silent — nothing was playing on the default render device " +
                                  $"(expected on headless/CI). Loopback active={loopbackActive}.");

            Console.WriteLine();
            Console.WriteLine(info.Ok ? "RESULT: ALL CHECKS PASSED" : "RESULT: FAILED");
            return info.Ok ? 0 : 1;
        }

        private static void GenerateSyntheticVideo(Recorder recorder, VideoParams v, int totalFrames, long t0, long freq)
        {
            int frameBytes = v.Width * v.Height * 3 / 2;
            nint nv12 = Marshal.AllocHGlobal(frameBytes);
            try
            {
                for (int k = 0; k < totalFrames; k++)
                {
                    Nv12TestPattern.Fill(nv12, v.Width, v.Width, v.Height, k);
                    long ts = t0 + (long)k * freq / v.FpsNumerator;
                    recorder.PushVideoFrame(new RawVideoFrame(nv12, v.Width, v.Width, v.Height, PixelFormat.NV12, ts));
                    // Pace to real time so the capture has time to deliver audio matching the duration.
                    long target = t0 + (long)(k + 1) * freq / v.FpsNumerator;
                    while (Stopwatch.GetTimestamp() < target)
                        Thread.SpinWait(50);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(nv12);
            }
        }
    }
}
