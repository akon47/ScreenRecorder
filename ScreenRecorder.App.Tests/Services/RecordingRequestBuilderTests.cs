using System;
using System.IO;
using System.Windows;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Encoder;
using ScreenRecorder.Models;
using ScreenRecorder.Services;

namespace ScreenRecorder.AppTests.Services
{
    [TestClass]
    public sealed class RecordingRequestBuilderTests
    {
        private static readonly ContainerFormat Mp4 = ContainerFormat.FromFormat("mp4");

        private static AppSettings Advanced(Action<AppSettings> configure = null)
        {
            var s = new AppSettings { AdvancedSettings = true };
            configure?.Invoke(s);
            return s;
        }

        #region Which settings apply

        [TestMethod]
        public void AdvancedOff_UsesTheSafeDefaults_WhateverIsStored()
        {
            var s = new AppSettings
            {
                AdvancedSettings = false,
                VideoCodec = VideoCodec.H265,
                AudioCodec = AudioCodec.Mp3,
                QualityMode = RecordQualityMode.High,
                FrameRate = 30,
            };

            Assert.AreEqual(VideoCodec.H264, RecordingRequestBuilder.EffectiveVideoCodec(s));
            Assert.AreEqual(AudioCodec.Aac, RecordingRequestBuilder.EffectiveAudioCodec(s));
            Assert.AreEqual(RecordQualityMode.Bitrate, RecordingRequestBuilder.EffectiveQualityMode(s));
            Assert.AreEqual(AutoFrameRate.Auto, RecordingRequestBuilder.EffectiveFrameRate(s));
        }

        [TestMethod]
        public void AdvancedOn_UsesTheStoredChoices()
        {
            AppSettings s = Advanced(x =>
            {
                x.VideoCodec = VideoCodec.H265;
                x.AudioCodec = AudioCodec.Mp3;
                x.QualityMode = RecordQualityMode.Low;
                x.FrameRate = 24;
            });

            Assert.AreEqual(VideoCodec.H265, RecordingRequestBuilder.EffectiveVideoCodec(s));
            Assert.AreEqual(AudioCodec.Mp3, RecordingRequestBuilder.EffectiveAudioCodec(s));
            Assert.AreEqual(RecordQualityMode.Low, RecordingRequestBuilder.EffectiveQualityMode(s));
            Assert.AreEqual(24, RecordingRequestBuilder.EffectiveFrameRate(s));
        }

        [DataTestMethod]
        [DataRow("mp4", VideoCodec.Av1)]
        [DataRow("matroska", VideoCodec.Av1)]
        [DataRow("avi", VideoCodec.H264)]
        [DataRow("mpegts", VideoCodec.H264)]
        [DataRow("mov", VideoCodec.H264)]
        public void Av1_IsUsedOnlyWhereTheFileFormatCanHoldIt(string format, VideoCodec expected)
        {
            AppSettings s = Advanced(x =>
            {
                x.RecordFormat = format;
                x.VideoCodec = VideoCodec.Av1;
            });

            Assert.AreEqual(expected, RecordingRequestBuilder.EffectiveVideoCodec(s));
        }

        #endregion

        #region Container

        [TestMethod]
        public void ResolveContainer_Video_IsTheSelectedFormat()
        {
            ContainerFormat container = RecordingRequestBuilder.ResolveContainer(new AppSettings { RecordFormat = "matroska" }, RecordingKind.Video, audioOnly: false);

            Assert.AreEqual("matroska", container.Format);
            Assert.AreEqual("mkv", container.Extension);
        }

        [TestMethod]
        public void ResolveContainer_Gif_IsGifWhateverTheSelectedFormat()
        {
            ContainerFormat container = RecordingRequestBuilder.ResolveContainer(new AppSettings { RecordFormat = "avi" }, RecordingKind.Gif, audioOnly: false);

            Assert.AreEqual(RecordingRequestBuilder.GifFormat, container.Format);
            Assert.AreEqual("gif", container.Extension);
        }

        [TestMethod]
        public void ResolveContainer_AudioOnly_FollowsTheAudioCodec()
        {
            Assert.AreEqual("m4a", RecordingRequestBuilder.ResolveContainer(new AppSettings(), RecordingKind.Video, audioOnly: true).Extension);
            Assert.AreEqual("mp3", RecordingRequestBuilder.ResolveContainer(Advanced(x => x.AudioCodec = AudioCodec.Mp3), RecordingKind.Video, audioOnly: true).Extension);
            Assert.IsNull(RecordingRequestBuilder.ResolveContainer(Advanced(x => x.AudioCodec = AudioCodec.None), RecordingKind.Video, audioOnly: true),
                "no audio codec = nothing to record");
        }

        [TestMethod]
        public void ResolveContainer_UnknownFormat_IsNull()
        {
            Assert.IsNull(RecordingRequestBuilder.ResolveContainer(new AppSettings { RecordFormat = "nope" }, RecordingKind.Video, audioOnly: false));
        }

        #endregion

        #region File name and region

        [TestMethod]
        public void BuildFilePath_UsesTheAppNameAndATimestamp()
        {
            string path = RecordingRequestBuilder.BuildFilePath(@"C:\Videos", new DateTime(2026, 1, 2, 3, 4, 5, 678), "mp4");

            Assert.AreEqual(@"C:\Videos", Path.GetDirectoryName(path));
            Assert.AreEqual("ScreenRecorder-20260102-030405.678.mp4", Path.GetFileName(path));
        }

        [TestMethod]
        public void NormalizeRegion_MakesTheSizeEven()
        {
            Rect? region = RecordingRequestBuilder.NormalizeRegion(new Rect(11, 22, 641, 481));

            Assert.AreEqual(new Rect(11, 22, 640, 480), region.Value);
        }

        [DataTestMethod]
        [DataRow(99.0, 400.0)]
        [DataRow(400.0, 99.0)]
        [DataRow(100.9, 100.0)] // 100.9 → 100 after truncation: still valid on that side...
        public void NormalizeRegion_SmallerThanTheMinimum_IsRejected(double width, double height)
        {
            Rect? region = RecordingRequestBuilder.NormalizeRegion(new Rect(0, 0, width, height));

            bool tooSmall = (int)width < RecordingRequestBuilder.MinimumRegionSize || (int)height < RecordingRequestBuilder.MinimumRegionSize;
            Assert.AreEqual(!tooSmall, region.HasValue);
        }

        [TestMethod]
        public void NormalizeRegion_Empty_IsRejected()
        {
            Assert.IsNull(RecordingRequestBuilder.NormalizeRegion(Rect.Empty));
        }

        #endregion

        #region Build

        [TestMethod]
        public void Build_Display_CarriesTheBasicSettings()
        {
            var s = new AppSettings { VideoBitrate = 8000000, AudioBitrate = 192000, CaptureCursor = false, RecordMicrophone = true };

            RecordingOptions o = RecordingRequestBuilder.Build(s, ResolvedCaptureTarget.ForDisplay(@"\\.\DISPLAY1"), Mp4, @"C:\out.mp4");

            Assert.AreEqual("mp4", o.Format);
            Assert.AreEqual(@"C:\out.mp4", o.Url);
            Assert.AreEqual(@"\\.\DISPLAY1", o.DeviceName);
            Assert.AreEqual(ResolvedCaptureTarget.FullScreen, o.Region);
            Assert.IsTrue(o.DesktopRegion.IsEmpty);
            Assert.AreEqual(IntPtr.Zero, o.WindowHandle);
            Assert.AreEqual(VideoCodec.H264, o.VideoCodec);
            Assert.AreEqual(8000000, o.VideoBitrate);
            Assert.AreEqual(192000, o.AudioBitrate);
            Assert.AreEqual(RateControl.Cbr, o.RateControl);
            Assert.AreEqual(AutoFrameRate.Auto, o.FrameRate);
            Assert.IsFalse(o.DrawCursor);
            Assert.IsTrue(o.RecordSystemAudio);
            Assert.IsTrue(o.RecordMicrophone);
            Assert.IsFalse(o.AudioOnly);
            Assert.IsFalse(o.IsGif);
        }

        [DataTestMethod]
        [DataRow(RecordQualityMode.Bitrate, RateControl.Cbr, 23)]
        [DataRow(RecordQualityMode.High, RateControl.Cq, 18)]
        [DataRow(RecordQualityMode.Medium, RateControl.Cq, 23)]
        [DataRow(RecordQualityMode.Low, RateControl.Cq, 28)]
        public void Build_QualityMode_MapsToRateControl(RecordQualityMode mode, RateControl expectedRateControl, int expectedQuality)
        {
            RecordingOptions o = RecordingRequestBuilder.Build(Advanced(x => x.QualityMode = mode), ResolvedCaptureTarget.ForDisplay("d"), Mp4, "f");

            Assert.AreEqual(expectedRateControl, o.RateControl);
            Assert.AreEqual(expectedQuality, o.Quality);
        }

        [TestMethod]
        public void Build_AudioDevices_ApplyOnlyWithAdvancedSettings()
        {
            var basic = new AppSettings { SystemAudioDevice = "{out}", MicrophoneDevice = "{in}" };
            RecordingOptions simple = RecordingRequestBuilder.Build(basic, ResolvedCaptureTarget.ForDisplay("d"), Mp4, "f");
            Assert.IsNull(simple.SystemAudioDeviceId, "advanced off → system default devices");
            Assert.IsNull(simple.MicrophoneDeviceId);

            basic.AdvancedSettings = true;
            RecordingOptions advanced = RecordingRequestBuilder.Build(basic, ResolvedCaptureTarget.ForDisplay("d"), Mp4, "f");
            Assert.AreEqual("{out}", advanced.SystemAudioDeviceId);
            Assert.AreEqual("{in}", advanced.MicrophoneDeviceId);
        }

        [TestMethod]
        public void Build_Region_PassesTheMonitorRelativeRectangle()
        {
            var region = new Rect(100, 50, 640, 480);
            var target = ResolvedCaptureTarget.ForRegion("d", region, new Rect(2660, 50, 640, 480));

            RecordingOptions o = RecordingRequestBuilder.Build(new AppSettings(), target, Mp4, "f");

            Assert.AreEqual(region, o.Region);
            Assert.IsTrue(o.DesktopRegion.IsEmpty);
            Assert.AreEqual(new Rect(2660, 50, 640, 480), target.FrameRegion, "the on-screen outline uses desktop coordinates");
        }

        [TestMethod]
        public void Build_Window_PassesTheHandle_AndTheRegionFallbackDropsIt()
        {
            var handle = new IntPtr(0x1234);
            var target = ResolvedCaptureTarget.ForWindow(handle, "d", new Rect(10, 10, 800, 600), new Rect(10, 10, 800, 600));

            Assert.AreEqual(handle, RecordingRequestBuilder.Build(new AppSettings(), target, Mp4, "f").WindowHandle);

            ResolvedCaptureTarget fallback = target.AsRegion();
            RecordingOptions o = RecordingRequestBuilder.Build(new AppSettings(), fallback, Mp4, "f");
            Assert.AreEqual(IntPtr.Zero, o.WindowHandle);
            Assert.AreEqual(new Rect(10, 10, 800, 600), o.Region);
            Assert.AreEqual(target.FrameRegion, fallback.FrameRegion);
        }

        [TestMethod]
        public void Build_DesktopRegion_SpanningMonitors()
        {
            var desktop = new Rect(2300, 400, 600, 400);

            ResolvedCaptureTarget outlined = ResolvedCaptureTarget.ForDesktopRegion(desktop, showFrame: true);
            Assert.AreEqual(desktop, RecordingRequestBuilder.Build(new AppSettings(), outlined, Mp4, "f").DesktopRegion);
            Assert.AreEqual(desktop, outlined.FrameRegion);

            ResolvedCaptureTarget allDisplays = ResolvedCaptureTarget.ForDesktopRegion(desktop, showFrame: false);
            Assert.IsTrue(allDisplays.FrameRegion.IsEmpty, "all displays: nothing to outline");
        }

        [TestMethod]
        public void Build_AudioOnly()
        {
            ContainerFormat m4a = RecordingRequestBuilder.ResolveContainer(new AppSettings(), RecordingKind.Video, audioOnly: true);

            RecordingOptions o = RecordingRequestBuilder.Build(new AppSettings(), ResolvedCaptureTarget.ForAudioOnly(), m4a, "f.m4a");

            Assert.IsTrue(o.AudioOnly);
            Assert.AreEqual("ipod", o.Format);
            Assert.IsTrue(o.HasAudio);
        }

        [TestMethod]
        public void Build_Gif_HasNoSound_AndDefaultRateAndSize()
        {
            var s = new AppSettings { GifFrameRate = 30, GifScalePercent = 50, RecordMicrophone = true }; // advanced off
            ContainerFormat gif = RecordingRequestBuilder.ResolveContainer(s, RecordingKind.Gif, audioOnly: false);

            RecordingOptions o = RecordingRequestBuilder.Build(s, ResolvedCaptureTarget.ForRegion("d", new Rect(0, 0, 400, 300), new Rect(0, 0, 400, 300)), gif, "f.gif");

            Assert.IsTrue(o.IsGif);
            Assert.IsFalse(o.HasAudio);
            Assert.IsFalse(o.RecordMicrophone);
            Assert.IsFalse(o.RecordSystemAudio);
            Assert.AreEqual(AppSettings.DefaultGifFrameRate, o.FrameRate);
            Assert.AreEqual(1.0, o.OutputScale, 1e-9);
            Assert.AreEqual(new Rect(0, 0, 400, 300), o.Region);
        }

        [TestMethod]
        public void Build_Gif_AdvancedRateAndSizeApply()
        {
            AppSettings s = Advanced(x =>
            {
                x.GifFrameRate = 30;
                x.GifScalePercent = 50;
            });
            ContainerFormat gif = RecordingRequestBuilder.ResolveContainer(s, RecordingKind.Gif, audioOnly: false);

            RecordingOptions o = RecordingRequestBuilder.Build(s, ResolvedCaptureTarget.ForDisplay("d"), gif, "f.gif");

            Assert.AreEqual(30, o.FrameRate);
            Assert.AreEqual(0.5, o.OutputScale, 1e-9);
        }

        [TestMethod]
        public void Build_NullArguments_Throw()
        {
            Assert.ThrowsException<ArgumentNullException>(() => RecordingRequestBuilder.Build(null, ResolvedCaptureTarget.ForDisplay("d"), Mp4, "f"));
            Assert.ThrowsException<ArgumentNullException>(() => RecordingRequestBuilder.Build(new AppSettings(), null, Mp4, "f"));
            Assert.ThrowsException<ArgumentNullException>(() => RecordingRequestBuilder.Build(new AppSettings(), ResolvedCaptureTarget.ForDisplay("d"), null, "f"));
        }

        #endregion
    }
}
