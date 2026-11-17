using System;
using System.IO;
using System.Windows;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.DirectX;
using ScreenRecorder.Encoder;
using ScreenRecorder.Tests.Support;

namespace ScreenRecorder.Tests.Encoding
{
    /// <summary>
    /// The engine's front door when idle, and the requests it must reject before it touches the
    /// GPU, the audio devices or the output file — after which it must still be stopped and
    /// reusable.
    /// </summary>
    [TestClass]
    public sealed class ScreenEncoderArgumentTests
    {
        private string _tempDir;

        [TestInitialize]
        public void SetUp() => _tempDir = TestFiles.CreateTempDirectory();

        [TestCleanup]
        public void TearDown() => TestFiles.DeleteDirectory(_tempDir);

        private string OutputPath => Path.Combine(_tempDir, "never-written.mp4");

        private void AssertStoppedAndNothingWritten(ScreenEncoder encoder)
        {
            Assert.IsFalse(encoder.IsRunning);
            Assert.AreEqual(EncoderStatus.Stop, encoder.Status);
            Assert.AreEqual(0, encoder.LiveVideoFrames);
            Assert.AreEqual(TimeSpan.Zero, encoder.Elapsed);
            Assert.IsFalse(encoder.LastRecordingCompleted);
            Assert.AreEqual(0, Directory.GetFiles(_tempDir).Length, "a rejected request must not create an output file");
        }

        /// <summary>Counts the lifecycle events a rejected Start must not raise.</summary>
        private static Func<int> CountEvents(ScreenEncoder encoder)
        {
            int events = 0;
            encoder.CaptureStarting += (s, e) => events++;
            encoder.Stopped += (s, e) => events++;
            encoder.Interrupted += (s, e) => events++;
            return () => events;
        }

        #region Idle

        [TestMethod]
        public void NewScreenEncoder_IsIdle()
        {
            using var encoder = new ScreenEncoder();

            Assert.IsFalse(encoder.IsRunning);
            Assert.AreEqual(EncoderStatus.Stop, encoder.Status);
            Assert.AreEqual(0, encoder.LiveVideoFrames);
            Assert.AreEqual(TimeSpan.Zero, encoder.Elapsed);
            Assert.IsFalse(encoder.LastRecordingCompleted);
            Assert.AreEqual(HwAccel.Software, encoder.ActiveHwAccel);
            Assert.IsNull(encoder.VideoEncoderName);
            Assert.IsFalse(encoder.UsesGpuInput);
            Assert.IsFalse(encoder.IsVariableFrameRate);
            Assert.AreEqual(0, encoder.GridFrameRate);
            Assert.AreEqual(0, encoder.VideoWidth);
            Assert.AreEqual(0, encoder.VideoHeight);

            RecordingStatistics statistics = encoder.GetStatistics();
            Assert.AreEqual(TimeSpan.Zero, statistics.Elapsed);
            Assert.AreEqual(0, statistics.BytesWritten);
            Assert.AreEqual(0, statistics.VideoFrames);
            Assert.AreEqual(0, statistics.DroppedFrames);
            Assert.AreEqual(0, statistics.LateTicks);
        }

        [TestMethod]
        public void StopPauseResume_WhenIdle_DoNothing()
        {
            using var encoder = new ScreenEncoder();
            Func<int> events = CountEvents(encoder);

            encoder.Pause();
            Assert.AreEqual(EncoderStatus.Stop, encoder.Status);
            encoder.Resume();
            Assert.AreEqual(EncoderStatus.Stop, encoder.Status);
            encoder.Stop();
            Assert.AreEqual(EncoderStatus.Stop, encoder.Status);

            Assert.AreEqual(0, events(), "no recording, no events");
        }

        [TestMethod]
        public void Dispose_WhenIdle_IsHarmlessAndRepeatable()
        {
            var encoder = new ScreenEncoder();
            Func<int> events = CountEvents(encoder);

            encoder.Dispose();
            encoder.Dispose();

            Assert.IsFalse(encoder.IsRunning);
            Assert.AreEqual(0, events());
        }

        [TestMethod]
        public void RecordingStoppedEventArgs_CarriesUrlAndCompletion()
        {
            var completed = new RecordingStoppedEventArgs(@"C:\out\a.mp4", true);
            var failed = new RecordingStoppedEventArgs(null, false);

            Assert.AreEqual(@"C:\out\a.mp4", completed.Url);
            Assert.IsTrue(completed.Completed);
            Assert.IsNull(failed.Url);
            Assert.IsFalse(failed.Completed);
        }

        #endregion

        #region Rejected requests

        [TestMethod]
        public void Start_NullOptions_Throws()
        {
            using var encoder = new ScreenEncoder();

            var ex = Assert.ThrowsExactly<ArgumentNullException>(() => encoder.Start(null));

            Assert.AreEqual("options", ex.ParamName);
            Assert.IsFalse(encoder.IsRunning);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void Start_WithoutOutputPath_Throws(string url)
        {
            using var encoder = new ScreenEncoder();
            Func<int> events = CountEvents(encoder);

            Assert.ThrowsExactly<ArgumentException>(() => encoder.Start(new RecordingOptions { Url = url, DeviceName = @"\\.\DISPLAY1" }));

            AssertStoppedAndNothingWritten(encoder);
            Assert.AreEqual(0, events());
        }

        [TestMethod]
        [DataRow(@"\\.\DISPLAY_DOES_NOT_EXIST")]
        [DataRow("")]
        [DataRow(null)]
        public void Start_UnknownMonitor_ThrowsAndStaysStopped(string deviceName)
        {
            using var encoder = new ScreenEncoder();
            Func<int> events = CountEvents(encoder);

            Assert.ThrowsExactly<ArgumentException>(() => encoder.Start(new RecordingOptions
            {
                Url = OutputPath,
                DeviceName = deviceName,
                RecordSystemAudio = false,
                AudioCodec = AudioCodec.None,
            }));

            AssertStoppedAndNothingWritten(encoder);
            Assert.AreEqual(0, events(), "the app must not be told a recording is starting (or stopped)");
        }

        [TestMethod]
        public void Start_Gif_UnknownMonitor_ThrowsAndStaysStopped()
        {
            using var encoder = new ScreenEncoder();
            Func<int> events = CountEvents(encoder);

            Assert.ThrowsExactly<ArgumentException>(() => encoder.Start(new RecordingOptions
            {
                Format = RecordingOptions.GifFormat,
                Url = Path.Combine(_tempDir, "never-written.gif"),
                DeviceName = @"\\.\DISPLAY_DOES_NOT_EXIST",
            }));

            AssertStoppedAndNothingWritten(encoder);
            Assert.AreEqual(0, events());
        }

        [TestMethod]
        public void Start_DesktopRegionOutsideEveryDisplay_ThrowsAndStaysStopped()
        {
            using var encoder = new ScreenEncoder();
            Func<int> events = CountEvents(encoder);

            Assert.ThrowsExactly<ArgumentException>(() => encoder.Start(new RecordingOptions
            {
                Url = OutputPath,
                DesktopRegion = new Rect(-900_000, -900_000, 640, 480),
                RecordSystemAudio = false,
                AudioCodec = AudioCodec.None,
            }));

            AssertStoppedAndNothingWritten(encoder);
            Assert.AreEqual(0, events());
        }

        [TestMethod]
        // No codec.
        [DataRow(AudioCodec.None, true, true)]
        // A codec but no source.
        [DataRow(AudioCodec.Aac, false, false)]
        [DataRow(AudioCodec.Mp3, false, false)]
        public void Start_AudioOnlyWithoutAnyAudio_ThrowsAndStaysStopped(AudioCodec codec, bool systemAudio, bool microphone)
        {
            using var encoder = new ScreenEncoder();
            Func<int> events = CountEvents(encoder);

            Assert.ThrowsExactly<InvalidOperationException>(() => encoder.Start(new RecordingOptions
            {
                Format = "ipod",
                Url = OutputPath,
                AudioOnly = true,
                AudioCodec = codec,
                RecordSystemAudio = systemAudio,
                RecordMicrophone = microphone,
            }));

            AssertStoppedAndNothingWritten(encoder);
            Assert.AreEqual(0, events());
        }

        [TestMethod]
        public void Start_AfterARejectedRequest_TheEncoderIsStillUsable()
        {
            using var encoder = new ScreenEncoder();

            for (int attempt = 0; attempt < 3; attempt++)
            {
                Assert.ThrowsExactly<ArgumentException>(() => encoder.Start(new RecordingOptions
                {
                    Url = OutputPath,
                    DeviceName = @"\\.\DISPLAY_DOES_NOT_EXIST",
                    AudioCodec = AudioCodec.None,
                }));
                Assert.IsFalse(encoder.IsRunning, $"attempt {attempt}: a failed Start left the encoder running");
            }

            // Still rejects for the right reason (it did not get stuck in a half-started state).
            Assert.ThrowsExactly<ArgumentNullException>(() => encoder.Start(null));
            encoder.Stop();
            Assert.AreEqual(EncoderStatus.Stop, encoder.Status);
        }

        [TestMethod]
        [TestCategory("Hardware")]
        public void Start_RegionOutsideTheMonitor_ThrowsAndStaysStopped()
        {
            MonitorInfo primary = MonitorInfo.GetPrimaryMonitorInfo();
            if (primary == null)
                Assert.Inconclusive("No display attached to this session.");

            using var encoder = new ScreenEncoder();
            Func<int> events = CountEvents(encoder);

            Assert.ThrowsExactly<ArgumentException>(() => encoder.Start(new RecordingOptions
            {
                Url = OutputPath,
                DeviceName = primary.DeviceName,
                Region = new Rect(primary.Width + 100, primary.Height + 100, 320, 240),
                RecordSystemAudio = false,
                AudioCodec = AudioCodec.None,
            }));

            AssertStoppedAndNothingWritten(encoder);
            Assert.AreEqual(0, events());
        }

        #endregion
    }
}
