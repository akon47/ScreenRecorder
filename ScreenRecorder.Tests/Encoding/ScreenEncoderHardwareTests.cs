using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.DirectX;
using ScreenRecorder.Encoder;
using ScreenRecorder.Tests.Support;
using Windows.Graphics.Capture;

namespace ScreenRecorder.Tests.Encoding
{
    /// <summary>
    /// End-to-end smoke tests of the real pipeline (Windows.Graphics.Capture → D3D11 → encoder →
    /// file) on the primary monitor, through the same entry point the app uses. They need an
    /// interactive desktop session and a GPU and report Inconclusive without them.
    /// </summary>
    [TestClass]
    [TestCategory("Hardware")]
    [TestCategory("Integration")]
    public sealed class ScreenEncoderHardwareTests
    {
        private const double RecordSeconds = 2.0;

        private static bool _hardwareProbed;

        private string _tempDir;

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void SetUp()
        {
            FFmpegTestEnvironment.RequireFFmpeg();

            bool supported = false;
            try
            {
                supported = GraphicsCaptureSession.IsSupported();
            }
            catch (Exception ex)
            {
                Assert.Inconclusive("Windows.Graphics.Capture is not available: " + ex.Message);
            }

            if (!supported)
                Assert.Inconclusive("Windows.Graphics.Capture is not supported on this system.");

            if (!_hardwareProbed)
            {
                // Once per run: which hardware encoders work here (what the app does at start-up).
                HardwareEncoders.Detect();
                _hardwareProbed = true;
            }

            _tempDir = TestFiles.CreateTempDirectory();
        }

        [TestCleanup]
        public void TearDown() => TestFiles.DeleteDirectory(_tempDir);

        private sealed class Result
        {
            public string Path;
            public double ElapsedSeconds;
            public int GridFrameRate;
            public bool VariableFrameRate;
            public int VideoWidth;
            public int VideoHeight;
            public RecordingStatistics Statistics;
            public MonitorInfo Monitor;
        }

        /// <summary>
        /// Records the primary monitor for about two seconds with the given options (output
        /// path and monitor are filled in) and checks the lifecycle around it.
        /// </summary>
        private Result RecordPrimaryMonitor(RecordingOptions options, string fileName)
        {
            MonitorInfo primary = MonitorInfo.GetPrimaryMonitorInfo();
            if (primary == null)
                Assert.Inconclusive("No display attached to this session.");

            string path = Path.Combine(_tempDir, fileName);
            options.Url = path;
            options.DeviceName = primary.DeviceName;
            options.AudioCodec = AudioCodec.None;
            options.RecordSystemAudio = false;
            options.RecordMicrophone = false;

            var result = new Result { Path = path, Monitor = primary };
            using var encoder = new ScreenEncoder();
            int captureStarting = 0;
            EncoderStatus statusAtCaptureStarting = EncoderStatus.Stop;
            var stopped = new List<RecordingStoppedEventArgs>();
            RecordingInterruptedEventArgs interruption = null;
            encoder.CaptureStarting += (s, e) =>
            {
                captureStarting++;
                statusAtCaptureStarting = encoder.Status;
            };
            encoder.Stopped += (s, e) => stopped.Add(e);
            encoder.Interrupted += (s, e) => interruption = e;

            try
            {
                encoder.Start(options);
            }
            catch (Exception ex)
            {
                // No interactive desktop / no usable graphics device (e.g. a service session).
                Assert.Inconclusive($"Screen capture could not be started here: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                Assert.IsTrue(encoder.IsRunning);
                Assert.AreEqual(EncoderStatus.Start, encoder.Status);
                Assert.AreEqual(1, captureStarting, "the app must be told exactly once so it can hide its own windows");
                Assert.AreEqual(EncoderStatus.Start, statusAtCaptureStarting);

                // A second Start while running is ignored.
                encoder.Start(options);
                Assert.AreEqual(1, captureStarting);

                result.GridFrameRate = encoder.GridFrameRate;
                result.VariableFrameRate = encoder.IsVariableFrameRate;
                result.VideoWidth = encoder.VideoWidth;
                result.VideoHeight = encoder.VideoHeight;
                TestContext.WriteLine($"Recording {primary.DeviceName} ({primary.PhysicalWidth}x{primary.PhysicalHeight} @ {primary.RefreshRate} Hz) as {options.Format}: " +
                                      $"{encoder.VideoWidth}x{encoder.VideoHeight}, grid {encoder.GridFrameRate} fps, variable={encoder.IsVariableFrameRate}, " +
                                      $"encoder={encoder.VideoEncoderName ?? "(none)"}, hw={encoder.ActiveHwAccel}, gpuInput={encoder.UsesGpuInput}");

                Thread.Sleep(TimeSpan.FromSeconds(RecordSeconds));

                result.Statistics = encoder.GetStatistics();
                result.ElapsedSeconds = encoder.Elapsed.TotalSeconds;
            }
            finally
            {
                encoder.Stop();
            }

            Assert.IsNull(interruption, $"the recording was interrupted: {interruption?.Reason} {interruption?.Error}");
            Assert.IsFalse(encoder.IsRunning);
            Assert.AreEqual(EncoderStatus.Stop, encoder.Status);
            Assert.AreEqual(TimeSpan.Zero, encoder.Elapsed, "the clock is reset after a recording");
            Assert.IsTrue(encoder.LastRecordingCompleted, "the file was not finalized");

            Assert.AreEqual(1, stopped.Count, "Stopped must be raised exactly once");
            Assert.AreEqual(path, stopped[0].Url);
            Assert.IsTrue(stopped[0].Completed);

            Assert.AreEqual(result.ElapsedSeconds, result.Statistics.Elapsed.TotalSeconds, 0.1, "statistics elapsed");
            Assert.IsTrue(result.ElapsedSeconds >= RecordSeconds - 0.05 && result.ElapsedSeconds < RecordSeconds + 1.0, $"elapsed {result.ElapsedSeconds:0.000} s");
            Assert.IsTrue(result.Statistics.VideoFrames > 0, "no frame was recorded");
            Assert.IsTrue(File.Exists(path));

            TestContext.WriteLine($"Elapsed {result.ElapsedSeconds:0.000} s: frames {result.Statistics.VideoFrames}, dropped {result.Statistics.DroppedFrames}, " +
                                  $"late {result.Statistics.LateTicks}, bytes {result.Statistics.BytesWritten}, file {new FileInfo(path).Length} bytes");
            return result;
        }

        private MediaProbe ProbeVideoFile(Result r)
        {
            MediaProbe probe = MediaProbe.Probe(r.Path);
            Assert.IsTrue(probe.Opened, "libavformat could not open the recording");

            // Full screen at physical resolution (even-aligned), video only.
            Assert.AreEqual(1, probe.StreamCount);
            Assert.AreEqual("h264", probe.VideoCodec);
            Assert.AreEqual(r.Monitor.PhysicalWidth & ~1, probe.Width);
            Assert.AreEqual(r.Monitor.PhysicalHeight & ~1, probe.Height);
            Assert.AreEqual(probe.Width, r.VideoWidth);
            Assert.AreEqual(probe.Height, r.VideoHeight);

            TestContext.WriteLine($"File: {probe.VideoPacketCount} packets, duration {probe.DurationSeconds:0.000} s, last PTS {probe.VideoPts.Last():0.000} s");
            return probe;
        }

        [TestMethod, Timeout(120_000)]
        public void FixedFrameRate_30Fps_RecordsOneFramePerGridTick()
        {
            Result r = RecordPrimaryMonitor(new RecordingOptions { Format = "mp4", VideoCodec = VideoCodec.H264, VideoBitrate = 8_000_000, FrameRate = 30 }, "screen-30fps.mp4");
            MediaProbe probe = ProbeVideoFile(r);

            Assert.AreEqual(30, r.GridFrameRate);
            Assert.IsFalse(r.VariableFrameRate);

            // Constant frame rate: fps × elapsed frames (2 s → 60).
            long dropped = r.Statistics.DroppedFrames;
            Assert.AreEqual(r.ElapsedSeconds * 30, probe.VideoPacketCount, 3 + dropped, "video packets for the elapsed time");
            Assert.AreEqual(60.0, probe.VideoPacketCount, 6 + dropped, "video packets for a 2 s recording");
            Assert.AreEqual(r.ElapsedSeconds, probe.DurationSeconds, 0.2, "duration");

            // Starts at 0 on the exact 1/30 s grid, strictly increasing.
            Assert.AreEqual(0.0, probe.VideoPts[0], 1e-6);
            Assert.IsTrue(probe.VideoKeyFrames[0]);
            for (int i = 1; i < probe.VideoPts.Count; i++)
            {
                double step = (probe.VideoPts[i] - probe.VideoPts[i - 1]) * 30;
                Assert.IsTrue(step > 0.5, $"timestamps not increasing at packet {i}");
                Assert.AreEqual(Math.Round(step), step, 1e-3, $"packet {i} is off the 1/30 s grid");
            }

            // Without drops there is no hole in the grid at all.
            if (dropped == 0)
            {
                for (int i = 0; i < probe.VideoPts.Count; i++)
                    Assert.AreEqual(i / 30.0, probe.VideoPts[i], 1e-6, $"PTS of packet {i}");
            }
        }

        [TestMethod, Timeout(120_000)]
        public void AutoFrameRate_RecordsOnlyChangedFramesOnTheMonitorGrid()
        {
            Result r = RecordPrimaryMonitor(new RecordingOptions { Format = "mp4", VideoCodec = VideoCodec.H264, VideoBitrate = 8_000_000, FrameRate = AutoFrameRate.Auto }, "screen-auto.mp4");
            MediaProbe probe = ProbeVideoFile(r);

            Assert.IsTrue(r.VariableFrameRate);
            Assert.AreEqual(AutoFrameRate.FromRefreshRate(r.Monitor.RefreshRate), r.GridFrameRate, "grid derived from the monitor's refresh rate");

            // Between "static screen" (first frame + keep-alives + closing frame) and "every tick".
            int grid = r.GridFrameRate;
            Assert.IsTrue(probe.VideoPacketCount >= 2, $"only {probe.VideoPacketCount} packets");
            Assert.IsTrue(probe.VideoPacketCount <= grid * r.ElapsedSeconds + 3,
                $"{probe.VideoPacketCount} packets exceed the {grid} fps grid over {r.ElapsedSeconds:0.00} s");

            // Every frame sits on the grid, starting at 0, strictly increasing.
            Assert.AreEqual(0.0, probe.VideoPts[0], 1e-6);
            Assert.IsTrue(probe.VideoKeyFrames[0]);
            for (int i = 1; i < probe.VideoPts.Count; i++)
            {
                double index = probe.VideoPts[i] * grid;
                Assert.IsTrue(probe.VideoPts[i] > probe.VideoPts[i - 1], $"timestamps not increasing at packet {i}");
                Assert.AreEqual(Math.Round(index), index, 1e-3, $"packet {i} at {probe.VideoPts[i]:0.####} s is off the 1/{grid} s grid");

                // A still screen is refreshed by a keep-alive at least once a second.
                Assert.IsTrue(probe.VideoPts[i] - probe.VideoPts[i - 1] <= 1.0 + 0.25, $"gap of {probe.VideoPts[i] - probe.VideoPts[i - 1]:0.###} s before packet {i}");
            }

            // The last frame is the closing frame at (about) the stop position.
            Assert.AreEqual(r.ElapsedSeconds, probe.VideoPts.Last(), 0.25, "last frame time");
        }

        [TestMethod, Timeout(120_000)]
        public void AutoFrameRate_VideoTrackIsAsLongAsTheRecording()
        {
            Result r = RecordPrimaryMonitor(new RecordingOptions { Format = "mp4", VideoCodec = VideoCodec.H264, VideoBitrate = 8_000_000, FrameRate = AutoFrameRate.Auto }, "screen-auto-duration.mp4");
            MediaProbe probe = ProbeVideoFile(r);

            Assert.AreEqual(RecordSeconds, probe.DurationSeconds, 0.5, "duration");
            Assert.AreEqual(r.ElapsedSeconds, probe.DurationSeconds, 0.25, "duration vs. the recording clock");
        }

        [TestMethod, Timeout(120_000)]
        public void Gif_RecordsAScaledAnimation()
        {
            Result r = RecordPrimaryMonitor(new RecordingOptions { Format = RecordingOptions.GifFormat, FrameRate = 10, OutputScale = 0.25 }, "screen.gif");

            Assert.AreEqual(10, r.GridFrameRate);
            Assert.IsTrue(r.VariableFrameRate, "a GIF only stores pictures that differ");

            // A quarter of the physical size, even.
            Assert.AreEqual(r.Monitor.PhysicalWidth / 4.0, r.VideoWidth, 2.0);
            Assert.AreEqual(r.Monitor.PhysicalHeight / 4.0, r.VideoHeight, 2.0);
            Assert.AreEqual(0, r.VideoWidth % 2);
            Assert.AreEqual(0, r.VideoHeight % 2);

            GifFile gif = GifFile.Read(r.Path);
            Assert.AreEqual(r.VideoWidth, gif.Width);
            Assert.AreEqual(r.VideoHeight, gif.Height);
            Assert.IsTrue(gif.LoopsForever);
            Assert.IsTrue(gif.Frames.Count >= 1 && gif.Frames.Count <= 10 * r.ElapsedSeconds + 2, $"{gif.Frames.Count} frames");

            double duration = gif.Frames.Sum(f => f.DelayCentiseconds) / 100.0;
            TestContext.WriteLine($"GIF: {gif.Width}x{gif.Height}, {gif.Frames.Count} frames, {duration:0.00} s, {new FileInfo(r.Path).Length} bytes");
            Assert.AreEqual(r.ElapsedSeconds, duration, 0.3, "total animation time");

            // The first picture is a real screenshot, not a blank canvas.
            uint[] first = gif.Frames[0].Canvas;
            Assert.IsTrue(first.Distinct().Take(8).Count() >= 8, "the first GIF frame has fewer than 8 distinct colors");
        }
    }
}
