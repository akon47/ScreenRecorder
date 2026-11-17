using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.Tests.Encoding
{
    [TestClass]
    public sealed class MediaFormatTests
    {
        [TestMethod]
        [DataRow("mp4", "MP4", "mp4")]
        [DataRow("avi", "AVI", "avi")]
        [DataRow("matroska", "Matroska", "mkv")]
        [DataRow("mpegts", "MPEG-TS", "ts")]
        [DataRow("mov", "QuickTime", "mov")]
        [DataRow("ipod", "M4A (AAC)", "m4a")]
        [DataRow("mp3", "MP3", "mp3")]
        [DataRow("gif", "GIF", "gif")]
        public void GetFormatInfo_KnownFormat_ReturnsNameAndExtension(string format, string expectedName, string expectedExtension)
        {
            MediaFormat.GetFormatInfo(format, out string longName, out string extensions);

            Assert.AreEqual(expectedName, longName);
            Assert.AreEqual(expectedExtension, extensions);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("MP4")]        // FFmpeg muxer names are case-sensitive
        [DataRow("mkv")]        // an extension, not a muxer name
        [DataRow("m4a")]
        [DataRow("ts")]
        [DataRow("webm")]
        [DataRow("GIF")]
        [DataRow(" mp4")]
        public void GetFormatInfo_UnknownFormat_ReturnsNulls(string format)
        {
            MediaFormat.GetFormatInfo(format, out string longName, out string extensions);

            Assert.IsNull(longName);
            Assert.IsNull(extensions);
        }

        [TestMethod]
        public void GetFormatInfo_GifFormatConstant_IsKnown()
        {
            MediaFormat.GetFormatInfo(RecordingOptions.GifFormat, out string longName, out string extensions);

            Assert.AreEqual("GIF", longName);
            Assert.AreEqual("gif", extensions);
        }

        [TestMethod]
        [DataRow("mp4")]
        [DataRow("matroska")]
        [DataRow("mov")]
        [DataRow("avi")]
        [DataRow("mpegts")]
        public void SupportsVideoCodec_H264AndHevc_GoIntoEveryContainer(string format)
        {
            Assert.IsTrue(MediaFormat.SupportsVideoCodec(format, VideoCodec.H264));
            Assert.IsTrue(MediaFormat.SupportsVideoCodec(format, VideoCodec.Hevc));
            Assert.IsTrue(MediaFormat.SupportsVideoCodec(format, VideoCodec.H265));
        }

        [TestMethod]
        [DataRow("mp4", true)]
        [DataRow("matroska", true)]
        [DataRow("mov", false)]
        [DataRow("avi", false)]
        [DataRow("mpegts", false)]
        [DataRow("MP4", false)]   // muxer names are case-sensitive
        [DataRow("", false)]
        [DataRow(null, false)]
        public void SupportsVideoCodec_Av1_OnlyInMp4AndMatroska(string format, bool expected)
        {
            Assert.AreEqual(expected, MediaFormat.SupportsVideoCodec(format, VideoCodec.Av1));
        }

        [TestMethod]
        public void GetFormatInfo_DefaultRecordingFormat_IsKnown()
        {
            MediaFormat.GetFormatInfo(new RecordingOptions().Format, out string longName, out string extensions);

            Assert.AreEqual("MP4", longName);
            Assert.AreEqual("mp4", extensions);
        }
    }
}
