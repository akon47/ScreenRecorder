using System;
using System.Linq;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ScreenRecorder.Tests.Encoding
{
    /// <summary>Encoder selection — pure logic, no FFmpeg call.</summary>
    [TestClass]
    public sealed class FFmpegVideoEncoderTests
    {
        [TestMethod]
        [DataRow(VideoCodec.H264, HwAccel.Nvenc, "h264_nvenc")]
        [DataRow(VideoCodec.H264, HwAccel.Qsv, "h264_qsv")]
        [DataRow(VideoCodec.H264, HwAccel.Amf, "h264_amf")]
        [DataRow(VideoCodec.H264, HwAccel.Software, "libx264")]
        [DataRow(VideoCodec.Hevc, HwAccel.Nvenc, "hevc_nvenc")]
        [DataRow(VideoCodec.Hevc, HwAccel.Qsv, "hevc_qsv")]
        [DataRow(VideoCodec.Hevc, HwAccel.Amf, "hevc_amf")]
        [DataRow(VideoCodec.Hevc, HwAccel.Software, "libx265")]
        [DataRow(VideoCodec.Av1, HwAccel.Nvenc, "av1_nvenc")]
        [DataRow(VideoCodec.Av1, HwAccel.Qsv, "av1_qsv")]
        [DataRow(VideoCodec.Av1, HwAccel.Amf, "av1_amf")]
        [DataRow(VideoCodec.Av1, HwAccel.Software, "libsvtav1")]
        public void ResolveEncoderNames_MapsCodecAndBackendToTheFFmpegEncoder(VideoCodec codec, HwAccel hw, string expected)
        {
            string[] names = FFmpegVideoEncoder.ResolveEncoderNames(codec, hw);

            CollectionAssert.AreEqual(new[] { expected }, names);
        }

        [TestMethod]
        public void ResolveEncoderNames_H265_IsAnAliasOfHevc()
        {
            Assert.AreEqual(VideoCodec.Hevc, VideoCodec.H265);
            CollectionAssert.AreEqual(new[] { "hevc_nvenc" }, FFmpegVideoEncoder.ResolveEncoderNames(VideoCodec.H265, HwAccel.Nvenc));
            CollectionAssert.AreEqual(new[] { "libx265" }, FFmpegVideoEncoder.ResolveEncoderNames(VideoCodec.H265, HwAccel.Software));
        }

        [TestMethod]
        public void ResolveEncoderNames_UnknownBackend_FallsBackToSoftware()
        {
            CollectionAssert.AreEqual(new[] { "libx264" }, FFmpegVideoEncoder.ResolveEncoderNames(VideoCodec.H264, (HwAccel)99));
            CollectionAssert.AreEqual(new[] { "libx265" }, FFmpegVideoEncoder.ResolveEncoderNames(VideoCodec.Hevc, (HwAccel)(-1)));
            CollectionAssert.AreEqual(new[] { "libsvtav1" }, FFmpegVideoEncoder.ResolveEncoderNames(VideoCodec.Av1, (HwAccel)99));
        }

        [TestMethod]
        [DataRow(VideoCodec.None)]
        [DataRow(VideoCodec.Default)]
        [DataRow((VideoCodec)99)]
        public void ResolveEncoderNames_UnsupportedCodec_Throws(VideoCodec codec)
        {
            foreach (HwAccel hw in Enum.GetValues<HwAccel>())
            {
                var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FFmpegVideoEncoder.ResolveEncoderNames(codec, hw));
                Assert.AreEqual("codec", ex.ParamName);
            }
        }

        [TestMethod]
        public void ResolveEncoderNames_EveryBackendOfEverySupportedCodec_IsNamedAfterTheCodec()
        {
            foreach (HwAccel hw in Enum.GetValues<HwAccel>())
            {
                string h264 = FFmpegVideoEncoder.ResolveEncoderNames(VideoCodec.H264, hw)[0];
                string hevc = FFmpegVideoEncoder.ResolveEncoderNames(VideoCodec.Hevc, hw)[0];
                string av1 = FFmpegVideoEncoder.ResolveEncoderNames(VideoCodec.Av1, hw)[0];

                Assert.IsTrue(h264.Contains("264"), $"{hw}: '{h264}' is not an H.264 encoder");
                Assert.IsTrue(hevc.Contains("hevc") || hevc.Contains("265"), $"{hw}: '{hevc}' is not an H.265 encoder");
                Assert.IsTrue(av1.Contains("av1"), $"{hw}: '{av1}' is not an AV1 encoder");
                Assert.AreEqual(3, new[] { h264, hevc, av1 }.Distinct().Count());

                // Hardware encoders are named after their backend.
                string suffix = hw switch { HwAccel.Nvenc => "_nvenc", HwAccel.Qsv => "_qsv", HwAccel.Amf => "_amf", _ => null };
                if (suffix != null)
                {
                    foreach (string name in new[] { h264, hevc, av1 })
                        Assert.IsTrue(name.EndsWith(suffix, StringComparison.Ordinal), $"{hw}: '{name}'");
                }
            }
        }
    }
}
