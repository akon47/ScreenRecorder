using System;
using System.Windows;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.Tests.Encoding
{
    [TestClass]
    public sealed class RecordingOptionsTests
    {
        [TestMethod]
        public void Defaults_DescribeAFullScreenMp4RecordingWithSystemAudio()
        {
            var options = new RecordingOptions();

            Assert.AreEqual("mp4", options.Format);
            Assert.IsNull(options.Url);
            Assert.AreEqual(VideoCodec.H264, options.VideoCodec);
            Assert.AreEqual(5_000_000, options.VideoBitrate);
            Assert.AreEqual(RateControl.Cbr, options.RateControl);
            Assert.AreEqual(23, options.Quality);
            Assert.AreEqual(AudioCodec.Aac, options.AudioCodec);
            Assert.AreEqual(160_000, options.AudioBitrate);
            Assert.AreEqual(AutoFrameRate.Auto, options.FrameRate);
            Assert.IsTrue(AutoFrameRate.IsAuto(options.FrameRate), "the default frame rate is automatic");
            Assert.IsNull(options.DeviceName);
            Assert.AreEqual(IntPtr.Zero, options.WindowHandle);
            Assert.IsTrue(options.DrawCursor);
            Assert.IsTrue(options.RecordSystemAudio);
            Assert.IsNull(options.SystemAudioDeviceId);
            Assert.IsFalse(options.RecordMicrophone);
            Assert.IsNull(options.MicrophoneDeviceId);
            Assert.IsFalse(options.AudioOnly);
            Assert.IsTrue(options.HasAudio);
            Assert.IsFalse(options.IsGif);
            Assert.AreEqual(1.0, options.OutputScale);
            Assert.IsTrue(options.DesktopRegion.IsEmpty, "by default one monitor is recorded, not a desktop rectangle");
        }

        [TestMethod]
        public void GifConstants_AreWhatTheAppPersists()
        {
            Assert.AreEqual("gif", RecordingOptions.GifFormat);
            Assert.AreEqual(15, RecordingOptions.DefaultGifFrameRate);
        }

        [TestMethod]
        [DataRow("gif", true)]
        [DataRow("GIF", true)]
        [DataRow("Gif", true)]
        [DataRow("mp4", false)]
        [DataRow("gifv", false)]
        [DataRow(" gif", false)]
        [DataRow("", false)]
        [DataRow(null, false)]
        public void IsGif_MatchesTheGifFormatIgnoringCase(string format, bool expected)
        {
            Assert.AreEqual(expected, new RecordingOptions { Format = format }.IsGif);
        }

        [TestMethod]
        public void HasAudio_IsAlwaysFalseForGif()
        {
            var options = new RecordingOptions
            {
                Format = RecordingOptions.GifFormat,
                AudioCodec = AudioCodec.Aac,
                RecordSystemAudio = true,
                RecordMicrophone = true,
            };

            Assert.IsFalse(options.HasAudio, "a GIF has no sound");

            options.Format = "mp4";
            Assert.IsTrue(options.HasAudio);
        }

        [TestMethod]
        public void DefaultRegion_CoversAnyMonitor()
        {
            var options = new RecordingOptions();

            foreach (var monitor in new[] { new Rect(0, 0, 1920, 1080), new Rect(0, 0, 7680, 4320) })
                Assert.AreEqual(monitor, Rect.Intersect(options.Region, monitor), "the default region must select the whole monitor");
        }

        [TestMethod]
        // codec, system audio, microphone → has audio
        [DataRow(AudioCodec.Aac, true, false, true)]
        [DataRow(AudioCodec.Aac, false, true, true)]
        [DataRow(AudioCodec.Aac, true, true, true)]
        [DataRow(AudioCodec.Aac, false, false, false)]
        [DataRow(AudioCodec.Mp3, true, false, true)]
        [DataRow(AudioCodec.Mp3, false, true, true)]
        [DataRow(AudioCodec.Mp3, true, true, true)]
        [DataRow(AudioCodec.Mp3, false, false, false)]
        [DataRow(AudioCodec.None, true, false, false)]
        [DataRow(AudioCodec.None, false, true, false)]
        [DataRow(AudioCodec.None, true, true, false)]
        [DataRow(AudioCodec.None, false, false, false)]
        public void HasAudio_NeedsACodecAndAtLeastOneSource(AudioCodec codec, bool systemAudio, bool microphone, bool expected)
        {
            var options = new RecordingOptions
            {
                AudioCodec = codec,
                RecordSystemAudio = systemAudio,
                RecordMicrophone = microphone,
            };

            Assert.AreEqual(expected, options.HasAudio);
        }

        [TestMethod]
        public void HasAudio_DoesNotDependOnAudioOnlyOrDeviceIds()
        {
            var options = new RecordingOptions
            {
                AudioOnly = true,
                AudioCodec = AudioCodec.None,
                SystemAudioDeviceId = "{0.0.0.00000000}.{some-device}",
                MicrophoneDeviceId = "{0.0.1.00000000}.{some-device}",
            };

            Assert.IsFalse(options.HasAudio, "a device id does not enable a source");

            options.AudioCodec = AudioCodec.Aac;
            options.RecordSystemAudio = false;
            options.RecordMicrophone = false;
            Assert.IsFalse(options.HasAudio);

            options.RecordMicrophone = true;
            Assert.IsTrue(options.HasAudio);
        }

        [TestMethod]
        public void RecordingStatistics_StoresWhatItIsGiven()
        {
            var statistics = new RecordingStatistics(TimeSpan.FromSeconds(12.5), 1_234_567, 750, 3, 2);

            Assert.AreEqual(TimeSpan.FromSeconds(12.5), statistics.Elapsed);
            Assert.AreEqual(1_234_567, statistics.BytesWritten);
            Assert.AreEqual(750, statistics.VideoFrames);
            Assert.AreEqual(3, statistics.DroppedFrames);
            Assert.AreEqual(2, statistics.LateTicks);
        }

        [TestMethod]
        public void RecordingInterruptedEventArgs_CarriesReasonAndError()
        {
            var error = new InvalidOperationException("disk full");

            var withError = new RecordingInterruptedEventArgs(RecordingInterruption.WriteFailed, error);
            var withoutError = new RecordingInterruptedEventArgs(RecordingInterruption.TargetClosed, null);

            Assert.AreEqual(RecordingInterruption.WriteFailed, withError.Reason);
            Assert.AreSame(error, withError.Error);
            Assert.AreEqual(RecordingInterruption.TargetClosed, withoutError.Reason);
            Assert.IsNull(withoutError.Error);
        }
    }
}
