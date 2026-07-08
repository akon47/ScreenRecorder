using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using MediaEncoder;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.EncoderHarness
{
    internal static unsafe class Program
    {
        private static int Main(string[] args)
        {
            ffmpeg.RootPath = AppContext.BaseDirectory;

            if (Array.Exists(args, a => a.Equals("--wgc-check", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("WGC GraphicsCaptureSession.IsSupported: " + Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported());
                Console.WriteLine("WGC projection available (compiled against Windows.Graphics.Capture).");
                return 0;
            }

            if (Array.Exists(args, a => a.Equals("--wgc-item", StringComparison.OrdinalIgnoreCase)))
            {
                using var dev = new ScreenRecorder.DirectX.Direct3D11Device();
                Console.WriteLine("D3D11 device created. FeatureLevel=" + dev.FeatureLevel);
                var winrt = ScreenRecorder.DirectX.WgcInterop.CreateWinRtDevice(dev.Device);
                Console.WriteLine("WinRT IDirect3DDevice bridged: " + (winrt != null));
                var mon = ScreenRecorder.DirectX.MonitorInfo.GetPrimaryMonitorInfo();
                Console.WriteLine($"Primary monitor: {mon.DeviceName} virtual {mon.Width}x{mon.Height}, physical {mon.PhysicalWidth}x{mon.PhysicalHeight}");
                var hmon = ScreenRecorder.DirectX.DisplayHelper.GetMonitorHandleFromDeviceName(mon.DeviceName);
                Console.WriteLine("HMONITOR: 0x" + hmon.ToString("X"));
                var item = ScreenRecorder.DirectX.WgcInterop.CreateItemForMonitor(hmon);
                Console.WriteLine($"GraphicsCaptureItem.Size: {item.Size.Width}x{item.Size.Height}");
                // WGC item size is always physical pixels; virtual differs under DPI scaling (#58).
                Console.WriteLine(item.Size.Width == mon.PhysicalWidth && item.Size.Height == mon.PhysicalHeight
                    ? "OK: item size matches physical monitor size." : "WARN: item size differs from physical monitor size.");
                return 0;
            }

            if (Array.Exists(args, a => a.Equals("--dpi-flip", StringComparison.OrdinalIgnoreCase)))
            {
                // Diagnoses whether the FFmpeg hardware-codec probe (QSV loads D3D9/DXVA2) flips
                // this process's DPI awareness, and whether Screen.AllScreens caches stale bounds.
                void Dump(string phase)
                {
                    GetProcessDpiAwareness(IntPtr.Zero, out int awareness);
                    Console.WriteLine($"[{phase}] process DPI awareness = {awareness} (0=unaware 1=system 2=per-monitor)");
                    foreach (var m in ScreenRecorder.DirectX.MonitorInfo.GetActiveMonitorInfos())
                        Console.WriteLine($"[{phase}]   {m.DeviceName}: virtual {m.Width}x{m.Height}, physical {m.PhysicalWidth}x{m.PhysicalHeight}");
                }
                Dump("before probe");
                MediaEncoder.MediaWriter.CheckHardwareCodec();
                Dump("after probe ");
                return 0;
            }

            if (Array.Exists(args, a => a.Equals("--dpi-map", StringComparison.OrdinalIgnoreCase)))
            {
                // Pure-math check of MonitorInfo.VirtualToPhysical — the DPI-virtualized→physical
                // region mapping that fixes the quarter-frame recording at 200% scale (issue #58).
                int failures = 0;
                void Check(string name, System.Windows.Rect actual, System.Windows.Rect expected)
                {
                    bool ok = actual == expected;
                    if (!ok) failures++;
                    Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}: {actual}" + (ok ? "" : $" (expected {expected})"));
                }

                // 3840x2160 monitor at 200%, seen by a DPI-unaware process as 1920x1080.
                var m200 = new ScreenRecorder.DirectX.MonitorInfo { Right = 1920, Bottom = 1080, PhysicalWidth = 3840, PhysicalHeight = 2160 };
                Check("200% full screen", m200.VirtualToPhysical(new System.Windows.Rect(0, 0, 1920, 1080)), new System.Windows.Rect(0, 0, 3840, 2160));
                Check("200% sub-region", m200.VirtualToPhysical(new System.Windows.Rect(100, 50, 640, 360)), new System.Windows.Rect(200, 100, 1280, 720));
                Check("200% empty region", m200.VirtualToPhysical(System.Windows.Rect.Empty), System.Windows.Rect.Empty);

                // 2560x1440 at 150% → Windows virtualizes to 1707x960 (rounded, non-exact ratio).
                var m150 = new ScreenRecorder.DirectX.MonitorInfo { Right = 1707, Bottom = 960, PhysicalWidth = 2560, PhysicalHeight = 1440 };
                Check("150% full screen (rounded virtual size)", m150.VirtualToPhysical(new System.Windows.Rect(0, 0, 1707, 960)), new System.Windows.Rect(0, 0, 2560, 1440));
                Check("150% sub-region at far edge stays in bounds", m150.VirtualToPhysical(new System.Windows.Rect(1000, 500, 707, 460)), new System.Windows.Rect(1500, 750, 1060, 690));

                // 100% scale (or a DPI-aware process): identity.
                var m100 = new ScreenRecorder.DirectX.MonitorInfo { Right = 2560, Bottom = 1440, PhysicalWidth = 2560, PhysicalHeight = 1440 };
                Check("100% full screen (identity)", m100.VirtualToPhysical(new System.Windows.Rect(0, 0, 2560, 1440)), new System.Windows.Rect(0, 0, 2560, 1440));
                Check("100% sub-region (identity)", m100.VirtualToPhysical(new System.Windows.Rect(123, 45, 640, 480)), new System.Windows.Rect(123, 45, 640, 480));

                // Real monitors on this machine, for reference.
                foreach (var real in ScreenRecorder.DirectX.MonitorInfo.GetActiveMonitorInfos())
                    Console.WriteLine($"     {real.DeviceName}: virtual {real.Width}x{real.Height}, physical {real.PhysicalWidth}x{real.PhysicalHeight}");

                Console.WriteLine(failures == 0 ? "PASS: dpi-map" : $"FAIL: {failures} case(s)");
                return failures == 0 ? 0 : 1;
            }

            if (Array.Exists(args, a => a.Equals("--audio-probe", StringComparison.OrdinalIgnoreCase)))
            {
                var en = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                try
                {
                    var render = en.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Console);
                    Console.WriteLine("Default render (loopback src): " + render.FriendlyName);
                }
                catch (Exception ex) { Console.WriteLine("No default render device: " + ex.Message); }
                try
                {
                    var capture = en.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Console);
                    Console.WriteLine("Default capture (mic):         " + capture.FriendlyName);
                }
                catch (Exception ex) { Console.WriteLine("No default capture device (mic): " + ex.Message); }
                try
                {
                    using var loop = new NAudio.Wave.WasapiLoopbackCapture();
                    Console.WriteLine($"Loopback WaveFormat: {loop.WaveFormat} ({loop.WaveFormat.Encoding}, {loop.WaveFormat.SampleRate}Hz, {loop.WaveFormat.Channels}ch, {loop.WaveFormat.BitsPerSample}bit)");
                    long bytes = 0; int callbacks = 0;
                    loop.DataAvailable += (s, e) => { bytes += e.BytesRecorded; callbacks++; };
                    loop.StartRecording();
                    System.Threading.Thread.Sleep(1500);
                    loop.StopRecording();
                    System.Threading.Thread.Sleep(200);
                    Console.WriteLine($"Loopback captured {bytes} bytes over {callbacks} callbacks in ~1.5s.");
                }
                catch (Exception ex) { Console.WriteLine("Loopback capture unavailable (no render device): " + ex.Message); }

                try
                {
                    var micDev = en.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Console);
                    using var mic = new NAudio.CoreAudioApi.WasapiCapture(micDev);
                    Console.WriteLine($"Mic WaveFormat: {mic.WaveFormat} ({mic.WaveFormat.Encoding}, {mic.WaveFormat.SampleRate}Hz, {mic.WaveFormat.Channels}ch, {mic.WaveFormat.BitsPerSample}bit)");
                    long mbytes = 0; int mcb = 0;
                    mic.DataAvailable += (s, e) => { mbytes += e.BytesRecorded; mcb++; };
                    mic.StartRecording();
                    System.Threading.Thread.Sleep(1500);
                    mic.StopRecording();
                    System.Threading.Thread.Sleep(200);
                    Console.WriteLine($"Mic captured {mbytes} bytes over {mcb} callbacks in ~1.5s.");
                }
                catch (Exception ex) { Console.WriteLine("Mic capture unavailable: " + ex.Message); }
                return 0;
            }

            if (Array.Exists(args, a => a.Equals("--screen-encoder", StringComparison.OrdinalIgnoreCase)))
            {
                // Drives the P4 ScreenEncoder exactly as the WPF shell does (full end-to-end path).
                var eo = ParseArgs(args);
                bool mic = Array.Exists(args, a => a.Equals("--mic", StringComparison.OrdinalIgnoreCase));
                MediaEncoder.MediaWriter.CheckHardwareCodec();
                ScreenRecorder.Encoder.FrameRateProvider.Framerate = eo.Fps;
                string monName = GetOptionValue(args, "--monitor");
                var mon = monName != null
                    ? ScreenRecorder.DirectX.MonitorInfo.GetMonitorInfo(monName)
                    : ScreenRecorder.DirectX.MonitorInfo.GetPrimaryMonitorInfo();
                if (mon == null)
                {
                    Console.WriteLine($"Monitor not found: {monName}");
                    return 2;
                }
                Console.WriteLine($"=== ScreenEncoder end-to-end: {eo.Seconds}s @{eo.Fps}fps, mic={mic}, monitor {mon.DeviceName} (virtual {mon.Width}x{mon.Height}, physical {mon.PhysicalWidth}x{mon.PhysicalHeight}) ===");
                var enc = new ScreenRecorder.Encoder.ScreenEncoder();
                enc.Start("mp4", eo.Output, MediaEncoder.VideoCodec.H264, 8_000_000, MediaEncoder.AudioCodec.Aac, 160_000,
                    mon.DeviceName, new System.Windows.Rect(0, 0, double.MaxValue, double.MaxValue), drawCursor: true, recordMicrophone: mic);
                bool testPause = Array.Exists(args, a => a.Equals("--pause", StringComparison.OrdinalIgnoreCase));
                bool sampleSmooth = Array.Exists(args, a => a.Equals("--smooth", StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"Recording... status={enc.Status}");
                if (testPause)
                {
                    System.Threading.Thread.Sleep(eo.Seconds * 500);
                    enc.Pause();
                    Console.WriteLine($"Paused at {enc.LiveVideoFrames} frames; holding 2s (excluded from output)...");
                    System.Threading.Thread.Sleep(2000);
                    enc.Resume();
                    Console.WriteLine($"Resumed at {enc.LiveVideoFrames} frames.");
                    System.Threading.Thread.Sleep(eo.Seconds * 500);
                }
                else if (sampleSmooth)
                {
                    // Sample LiveVideoFrames at ~60Hz (the UI render rate) and report the per-sample
                    // deltas — proves the elapsed-time display advances ~1 frame/tick (smooth),
                    // not in 6-frame jumps like the old 100ms poll.
                    var deltas = new System.Collections.Generic.Dictionary<long, int>();
                    ulong prev = enc.LiveVideoFrames;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.Elapsed.TotalSeconds < eo.Seconds)
                    {
                        System.Threading.Thread.Sleep(16);
                        ulong now = enc.LiveVideoFrames;
                        long d = (long)(now - prev);
                        deltas[d] = deltas.TryGetValue(d, out var c) ? c + 1 : 1;
                        prev = now;
                    }
                    Console.WriteLine("LiveVideoFrames delta per ~16ms sample (smooth = mostly 1):");
                    foreach (var kv in deltas.OrderBy(k => k.Key))
                        Console.WriteLine($"  delta {kv.Key}: {kv.Value} samples");
                }
                else
                {
                    System.Threading.Thread.Sleep(eo.Seconds * 1000);
                }
                Console.WriteLine($"Frames recorded: {enc.LiveVideoFrames}");
                enc.Stop();
                System.Threading.Thread.Sleep(500);
                Console.WriteLine($"Stopped. status={enc.Status}");
                enc.Dispose();

                var info = MediaFileVerifier.Probe(eo.Output);
                Console.WriteLine($"  streams={info.StreamCount} video={info.VideoCodec} {info.Width}x{info.Height} audio={info.AudioCodec} " +
                                  $"vPackets={info.VideoPacketCount} dur={info.DurationSeconds:0.000}s");
                info.Check(info.StreamCount == 2, "stream count == 2");
                info.Check(info.VideoCodec == "h264", "video codec h264");
                info.Check(info.VideoPacketCount > eo.Fps, "recorded multiple seconds of frames");
                info.Check(info.DurationSeconds > 1.0, "duration > 1s");
                Console.WriteLine(info.Ok ? "RESULT: ALL CHECKS PASSED" : "RESULT: FAILED");
                return info.Ok ? 0 : 1;
            }

            if (Array.Exists(args, a => a.Equals("--audio-capture", StringComparison.OrdinalIgnoreCase)))
            {
                var ao = ParseArgs(args);
                bool useMic = Array.Exists(args, a => a.Equals("--mic", StringComparison.OrdinalIgnoreCase));
                MediaEncoder.MediaWriter.CheckHardwareCodec();
                HwAccel ahw = MediaEncoder.MediaWriter.IsSupportedNvencH264() ? HwAccel.Nvenc : HwAccel.Software;
                return AudioCaptureSmoke.Run(ao.Seconds, ao.Output, useMic, ahw);
            }

            if (Array.Exists(args, a => a.Equals("--screen", StringComparison.OrdinalIgnoreCase)))
            {
                var so = ParseArgs(args);
                string png = Path.ChangeExtension(so.Output, ".png");
                MediaEncoder.MediaWriter.CheckHardwareCodec();
                return ScreenCaptureSmoke.Run(so.Seconds, so.Fps, so.Output, png, cursor: true,
                    resolveHw: (codec, hw) => MediaEncoder.MediaWriter.IsSupportedNvencH264() ? HwAccel.Nvenc : HwAccel.Software,
                    deviceName: GetOptionValue(args, "--monitor"));
            }

            var opt = ParseArgs(args);

            PrintEnvironment();

            if (opt.SmokeOnly)
                return 0;

            Console.WriteLine();
            Console.WriteLine($"=== Recording test: {opt.Seconds}s {opt.Width}x{opt.Height}@{opt.Fps} " +
                              $"{opt.VideoCodec}/{opt.Hw} {opt.Format} audio={opt.AudioCodec} ===");
            Console.WriteLine("Output: " + opt.Output);

            HwAccel hw = ResolveHwAccel(opt);
            Console.WriteLine("Resolved HW accel: " + hw);

            var videoParams = new VideoParams
            {
                Codec = opt.VideoCodec,
                Hw = hw,
                Width = opt.Width,
                Height = opt.Height,
                FpsNumerator = opt.Fps,
                FpsDenominator = 1,
                Bitrate = 5_000_000,
                RateControl = RateControl.Cbr,
            };

            AudioParams audioParams = opt.AudioCodec == AudioCodec.None ? null : new AudioParams
            {
                Codec = opt.AudioCodec,
                SampleRate = 48000,
                Channels = 2,
                Bitrate = 160_000,
                Format = SampleFormat.S16,
            };

            int totalVideoFrames = opt.Fps * opt.Seconds;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            long freq = System.Diagnostics.Stopwatch.Frequency;

            using (var recorder = new Recorder(opt.Output, opt.Format, videoParams, audioParams))
            {
                recorder.Start();

                Task audioTask = audioParams == null ? Task.CompletedTask : Task.Run(() =>
                    GenerateAudio(recorder, audioParams, opt.Seconds, t0, freq));

                GenerateVideo(recorder, videoParams, totalVideoFrames, t0, freq);

                audioTask.Wait();
                recorder.Stop();

                Console.WriteLine($"Pushed {totalVideoFrames} video frames; dropped {recorder.DroppedFrames}.");
            }

            return Verify(opt, totalVideoFrames);
        }

        private static void GenerateVideo(Recorder recorder, VideoParams v, int totalFrames, long t0, long freq)
        {
            int frameBytes = v.Width * v.Height * 3 / 2; // NV12, stride == width
            nint nv12 = Marshal.AllocHGlobal(frameBytes);
            try
            {
                for (int k = 0; k < totalFrames; k++)
                {
                    Nv12TestPattern.Fill(nv12, v.Width, v.Width, v.Height, k);
                    long ts = t0 + (long)k * freq / v.FpsNumerator;
                    recorder.PushVideoFrame(new RawVideoFrame(nv12, v.Width, v.Width, v.Height, PixelFormat.NV12, ts));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(nv12);
            }
        }

        private static void GenerateAudio(Recorder recorder, AudioParams a, int seconds, long t0, long freq)
        {
            const int blockSamples = 1024;
            long totalSamples = (long)a.SampleRate * seconds;
            long cumulative = 0;
            var sine = new SinePcm(a.SampleRate, a.Channels);
            short[] block = new short[blockSamples * a.Channels];

            while (cumulative < totalSamples)
            {
                int n = (int)Math.Min(blockSamples, totalSamples - cumulative);
                sine.Fill(block, n);
                long ts = t0 + cumulative * freq / a.SampleRate;
                fixed (short* p = block)
                {
                    recorder.PushAudioFrame(new RawAudioFrame((nint)p, n, a.SampleRate, a.Channels, SampleFormat.S16, ts));
                }

                cumulative += n;
            }
        }

        private static int Verify(Options opt, int totalVideoFrames)
        {
            Console.WriteLine();
            Console.WriteLine("=== Verification (in-process libavformat) ===");
            var info = MediaFileVerifier.Probe(opt.Output);
            bool hasAudio = opt.AudioCodec != AudioCodec.None;
            string expectedVideo = opt.VideoCodec == VideoCodec.H264 ? "h264" : "hevc";
            string expectedAudio = opt.AudioCodec == AudioCodec.Aac ? "aac" : "mp3";

            Console.WriteLine($"  streams={info.StreamCount} video={info.VideoCodec} {info.Width}x{info.Height} " +
                              $"audio={info.AudioCodec} {info.SampleRate}Hz/{info.Channels}ch  " +
                              $"vPackets={info.VideoPacketCount} aPackets={info.AudioPacketCount} dur={info.DurationSeconds:0.000}s " +
                              $"firstV={info.FirstVideoPtsSeconds:0.000} firstA={info.FirstAudioPtsSeconds:0.000}");

            info.Check(info.StreamCount == (hasAudio ? 2 : 1), $"stream count == {(hasAudio ? 2 : 1)}");
            info.Check(info.VideoCodec == expectedVideo, $"video codec == {expectedVideo}");
            info.Check(info.Width == opt.Width && info.Height == opt.Height, $"dimensions == {opt.Width}x{opt.Height}");
            info.Check(info.VideoPacketCount == totalVideoFrames, $"CFR video frame count == {totalVideoFrames}");
            info.Check(Math.Abs(info.DurationSeconds - opt.Seconds) < 0.20, $"duration ~= {opt.Seconds}s");
            // Near zero (mp4/mov keep video at 0; mkv/ts normalize timestamps so the earliest
            // packet — audio, with encoder priming — is 0 and video shifts by the priming gap).
            info.Check(!double.IsNaN(info.FirstVideoPtsSeconds) && Math.Abs(info.FirstVideoPtsSeconds) < 0.05, "first video PTS near 0 (<50ms)");

            if (hasAudio)
            {
                info.Check(info.AudioCodec == expectedAudio, $"audio codec == {expectedAudio}");
                info.Check(info.SampleRate == 48000, "audio sample rate == 48000");
                info.Check(info.Channels == 2, "audio channels == 2");
                info.Check(!double.IsNaN(info.FirstAudioPtsSeconds) && Math.Abs(info.FirstAudioPtsSeconds) < 0.05, "first audio PTS ~= 0");
                info.Check(Math.Abs(info.FirstVideoPtsSeconds - info.FirstAudioPtsSeconds) < 0.05, "A/V start aligned (<50ms)");
            }

            Console.WriteLine();
            Console.WriteLine(info.Ok ? "RESULT: ALL CHECKS PASSED" : "RESULT: FAILED");
            return info.Ok ? 0 : 1;
        }

        private static HwAccel ResolveHwAccel(Options opt)
        {
            if (opt.Hw == "nvenc") return HwAccel.Nvenc;
            if (opt.Hw == "qsv") return HwAccel.Qsv;
            if (opt.Hw == "sw") return HwAccel.Software;

            // auto
            if (opt.VideoCodec == VideoCodec.H264)
                return MediaWriter.IsSupportedNvencH264() ? HwAccel.Nvenc
                    : MediaWriter.IsSupportedQsvH264() ? HwAccel.Qsv : HwAccel.Software;
            return MediaWriter.IsSupportedNvencHEVC() ? HwAccel.Nvenc
                : MediaWriter.IsSupportedQsvHEVC() ? HwAccel.Qsv : HwAccel.Software;
        }

        private static void PrintEnvironment()
        {
            Console.WriteLine("=== FFmpeg ===");
            Console.WriteLine("av_version_info : " + ffmpeg.av_version_info());
            Console.WriteLine();
            Console.WriteLine("=== Hardware encoder probe ===");
            MediaWriter.CheckHardwareCodec();
            Console.WriteLine("  NVENC H264 : " + MediaWriter.IsSupportedNvencH264());
            Console.WriteLine("  QSV   H264 : " + MediaWriter.IsSupportedQsvH264());
            Console.WriteLine("  NVENC HEVC : " + MediaWriter.IsSupportedNvencHEVC());
            Console.WriteLine("  QSV   HEVC : " + MediaWriter.IsSupportedQsvHEVC());
        }

        private sealed class Options
        {
            public int Seconds = 5;
            public int Fps = 60;
            public int Width = 1280;
            public int Height = 720;
            public VideoCodec VideoCodec = VideoCodec.H264;
            public string Hw = "auto";
            public string Format = "mp4";
            public AudioCodec AudioCodec = AudioCodec.Aac;
            public string Output = Path.Combine(Path.GetTempPath(), "screenrecorder_harness.mp4");
            public bool SmokeOnly;
        }

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern int GetProcessDpiAwareness(IntPtr hProcess, out int awareness);

        private static string GetOptionValue(string[] args, string option)
        {
            int i = Array.FindIndex(args, a => a.Equals(option, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static Options ParseArgs(string[] args)
        {
            var o = new Options();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                    continue;
                string key = args[i].TrimStart('-');
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    map[key] = args[i + 1];
                    i++;
                }
                else
                {
                    map[key] = "true"; // value-less flag
                }
            }

            if (map.ContainsKey("smoke")) o.SmokeOnly = true;

            if (map.TryGetValue("seconds", out var s)) o.Seconds = int.Parse(s);
            if (map.TryGetValue("fps", out var f)) o.Fps = int.Parse(f);
            if (map.TryGetValue("w", out var w)) o.Width = int.Parse(w);
            if (map.TryGetValue("h", out var h)) o.Height = int.Parse(h);
            if (map.TryGetValue("codec", out var c)) o.VideoCodec = c.Equals("h265", StringComparison.OrdinalIgnoreCase) || c.Equals("hevc", StringComparison.OrdinalIgnoreCase) ? VideoCodec.Hevc : VideoCodec.H264;
            if (map.TryGetValue("hw", out var hw)) o.Hw = hw.ToLowerInvariant();
            if (map.TryGetValue("format", out var fmt)) o.Format = fmt;
            if (map.TryGetValue("audio", out var au)) o.AudioCodec = au.Equals("none", StringComparison.OrdinalIgnoreCase) ? AudioCodec.None : au.Equals("mp3", StringComparison.OrdinalIgnoreCase) ? AudioCodec.Mp3 : AudioCodec.Aac;
            if (map.TryGetValue("out", out var outp)) o.Output = outp;

            return o;
        }
    }
}
