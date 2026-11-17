using System;
using System.Collections.Generic;
using System.Linq;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Encoder;
using ScreenRecorder.Tests.Support;

namespace ScreenRecorder.Tests.Encoding
{
    /// <summary>Every container / codec combination the application offers yields a playable file.</summary>
    [TestClass]
    [TestCategory("Integration")]
    public sealed unsafe class RecorderContainerTests : RecorderTestBase
    {
        private const int Fps = 30;
        private const int Block = 1024;
        private const int SampleRate = 48000;

        /// <summary>Records <paramref name="frames"/> CFR frames plus the matching amount of audio.</summary>
        private Recorder Record(string path, string format, VideoCodec videoCodec, AudioCodec audioCodec, int frames, out int audioBlocks)
        {
            AudioParams audio = audioCodec == AudioCodec.None ? null : StereoAudio(audioCodec);
            var recorder = new Recorder(path, format, SoftwareVideo(Fps, codec: videoCodec), audio);
            recorder.Start();

            audioBlocks = audio == null ? 0 : (int)(frames * (long)SampleRate / Fps / Block);
            var sine = new SinePcm(SampleRate, 2);
            int pushed = 0;
            for (int i = 0; i < frames; i++)
            {
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
                if (audio != null)
                {
                    int due = Math.Min(audioBlocks, (int)((i + 1) * (long)SampleRate / Fps / Block));
                    RecorderFeed.PushSineS16(recorder, sine, due - pushed);
                    pushed = due;
                }
            }

            recorder.Stop();
            return recorder;
        }

        [TestMethod, Timeout(120_000)]
        [DataRow("mp4", "mp4")]
        [DataRow("matroska", "mkv")]
        [DataRow("mov", "mov")]
        [DataRow("mpegts", "ts")]
        [DataRow("avi", "avi")]
        public void H264WithAac_EveryContainer_IsPlayable(string format, string extension)
        {
            const int frames = 60; // 2 s
            string path = PathFor("container." + extension);

            // The extension the shell appends must be the one the format table advertises.
            MediaFormat.GetFormatInfo(format, out _, out string advertisedExtension);
            Assert.AreEqual(extension, advertisedExtension);

            using Recorder recorder = Record(path, format, VideoCodec.H264, AudioCodec.Aac, frames, out int audioBlocks);

            Assert.IsNull(recorder.Fault, recorder.Fault?.ToString());
            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.AreEqual(0, recorder.DroppedFrames);
            Assert.AreEqual(frames, recorder.EncodedVideoFrames);

            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(2, probe.StreamCount);
            Assert.AreEqual("h264", probe.VideoCodec);
            Assert.AreEqual(Width, probe.Width);
            Assert.AreEqual(Height, probe.Height);
            Assert.AreEqual("aac", probe.AudioCodec);
            Assert.AreEqual(SampleRate, probe.SampleRate);
            Assert.AreEqual(2, probe.Channels);

            Assert.AreEqual(frames, probe.VideoPacketCount, "video packets");
            Assert.IsTrue(probe.AudioPacketCount >= audioBlocks && probe.AudioPacketCount <= audioBlocks + 2,
                $"{probe.AudioPacketCount} audio packets for {audioBlocks} blocks");
            Assert.AreEqual(2.0, probe.DurationSeconds, 0.25, "duration");

            // Video timestamps advance by exactly one frame period (relative: MPEG-TS starts
            // at a non-zero offset; Matroska rounds to 1 ms).
            //
            // Exception: the FIRST gap. The first packet of the AAC encoder starts 1024 samples
            // (21.3 ms) before time 0 (priming). Where the muxer cannot express that, FFmpeg
            // shifts both streams forward by that amount and keeps the first video frame at 0,
            // i.e. it shows the first frame a little longer: MP4/MOV (hybrid-fragmented, no edit
            // list) 33.3 -> 54.7 ms; AVI rounds the shift up to one whole frame slot. A/V stay
            // aligned because the audio moves by the same amount.
            double firstGap = probe.VideoPts[1] - probe.VideoPts[0];
            Assert.IsTrue(firstGap >= 1.0 / Fps - 0.0011 && firstGap <= 2.0 / Fps + 0.0011, $"first video frame lasts {firstGap * 1000:0.0} ms");
            for (int i = 2; i < probe.VideoPts.Count; i++)
                Assert.AreEqual(1.0 / Fps, probe.VideoPts[i] - probe.VideoPts[i - 1], 0.0011, $"spacing before video packet {i}");

            // A/V start together in every container.
            Assert.IsTrue(Math.Abs(probe.VideoPts[0] - probe.AudioPts[0]) < 0.05,
                $"video starts at {probe.VideoPts[0]:0.###} s, audio at {probe.AudioPts[0]:0.###} s");

            Assert.IsTrue(probe.VideoKeyFrames[0], "the file must start with a keyframe");
            Assert.AreEqual(frames, MediaProbe.DecodeVideo(path).Count, "every frame must decode");
        }

        [TestMethod, Timeout(120_000)]
        [DataRow("mp4", "mp4")]
        [DataRow("matroska", "mkv")]
        [DataRow("mov", "mov")]
        [DataRow("mpegts", "ts")]
        [DataRow("avi", "avi")]
        public void H264WithoutAudio_EveryContainer_IsPlayable(string format, string extension)
        {
            const int frames = 45;
            string path = PathFor("video-only." + extension);

            using Recorder recorder = Record(path, format, VideoCodec.H264, AudioCodec.None, frames, out _);

            Assert.IsNull(recorder.Fault, recorder.Fault?.ToString());
            Assert.IsTrue(recorder.CompletedSuccessfully);

            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(1, probe.StreamCount);
            Assert.AreEqual("h264", probe.VideoCodec);
            Assert.IsFalse(probe.HasAudio);
            Assert.AreEqual(frames, probe.VideoPacketCount);
            Assert.AreEqual(frames / (double)Fps, probe.DurationSeconds, 0.25, "duration");

            // Without audio there is nothing to shift: every frame is exactly one period long.
            for (int i = 1; i < probe.VideoPts.Count; i++)
                Assert.AreEqual(1.0 / Fps, probe.VideoPts[i] - probe.VideoPts[i - 1], 0.0011, $"spacing before video packet {i}");
            Assert.IsTrue(probe.VideoKeyFrames[0], "the file must start with a keyframe");
        }

        [TestMethod, Timeout(120_000)]
        [DataRow("mp4", "mp4")]
        [DataRow("matroska", "mkv")]
        [DataRow("avi", "avi")]
        public void H264WithMp3_IsPlayable(string format, string extension)
        {
            const int frames = 60;
            string path = PathFor("mp3." + extension);

            using Recorder recorder = Record(path, format, VideoCodec.H264, AudioCodec.Mp3, frames, out int audioBlocks);

            Assert.IsNull(recorder.Fault, recorder.Fault?.ToString());
            Assert.IsTrue(recorder.CompletedSuccessfully);

            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(2, probe.StreamCount);
            Assert.AreEqual("h264", probe.VideoCodec);
            Assert.AreEqual("mp3", probe.AudioCodec);
            Assert.AreEqual(frames, probe.VideoPacketCount);

            int expectedMp3Frames = audioBlocks * Block / 1152;
            Assert.IsTrue(probe.AudioPacketCount >= expectedMp3Frames && probe.AudioPacketCount <= expectedMp3Frames + 3,
                $"{probe.AudioPacketCount} MP3 frames, expected about {expectedMp3Frames}");
            Assert.AreEqual(2.0, probe.DurationSeconds, 0.25, "duration");
        }

        [TestMethod, Timeout(120_000)]
        public void HevcSoftware_Mp4_HasExactTimestampsAndDecodes()
        {
            const int frames = 45;
            string path = PathFor("hevc.mp4");

            using Recorder recorder = Record(path, "mp4", VideoCodec.Hevc, AudioCodec.Aac, frames, out int audioBlocks);

            Assert.IsNull(recorder.Fault, recorder.Fault?.ToString());
            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.AreEqual(HwAccel.Software, recorder.ActualHwAccel);
            Assert.AreEqual(0, recorder.DroppedFrames);

            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(2, probe.StreamCount);
            Assert.AreEqual("hevc", probe.VideoCodec);
            Assert.AreEqual(Width, probe.Width);
            Assert.AreEqual(Height, probe.Height);
            Assert.AreEqual("aac", probe.AudioCodec);
            Assert.AreEqual(frames, probe.VideoPacketCount);
            Assert.IsTrue(probe.AudioPacketCount >= audioBlocks);

            // Frame 0 at 0; the rest one period apart (after the audio-priming shift of the first gap).
            Assert.AreEqual(0.0, probe.VideoPts[0], 1e-6);
            double firstGap = probe.VideoPts[1] - probe.VideoPts[0];
            Assert.IsTrue(firstGap >= 1.0 / Fps - 1e-6 && firstGap <= 1.0 / Fps + 1024.0 / SampleRate + 1e-4, $"first video frame lasts {firstGap * 1000:0.0} ms");
            for (int i = 0; i < frames; i++)
            {
                if (i >= 2)
                    Assert.AreEqual(1.0 / Fps, probe.VideoPts[i] - probe.VideoPts[i - 1], 1e-6, $"spacing before packet {i}");
                Assert.AreEqual(probe.VideoPts[i], probe.VideoDts[i], 1e-6, $"packet {i}: no reordering expected");
            }

            Assert.IsTrue(probe.VideoKeyFrames[0]);
            Assert.AreEqual(frames, MediaProbe.DecodeVideo(path).Count, "every frame must decode");
        }

        [TestMethod, Timeout(120_000)]
        public void HevcSoftware_DecodesToTheSubmittedPicture()
        {
            // libx265 does not take NV12: this goes through the swscale NV12 → planar path.
            string path = PathFor("hevc-picture.mp4");
            var video = SoftwareVideo(Fps, codec: VideoCodec.Hevc);
            video.RateControl = RateControl.Cq;
            video.Quality = 12;
            using (var recorder = new Recorder(path, "mp4", video, null))
            {
                recorder.Start();
                Assert.AreEqual("libx265", recorder.VideoEncoderName);
                RecorderFeed.SubmitSplit(recorder, Width, Height, Width + 64, 0, leftLuma: 60, rightLuma: 200, u: 90, v: 170);
                RecorderFeed.Repeat(recorder, 1);
                recorder.Stop();
                Assert.IsTrue(recorder.CompletedSuccessfully);
            }

            List<DecodedVideoFrame> decoded = MediaProbe.DecodeVideo(path);
            Assert.AreEqual(2, decoded.Count);
            foreach (DecodedVideoFrame frame in decoded)
            {
                foreach (int y in new[] { 2, Height / 2, Height - 3 })
                {
                    Assert.AreEqual(60, frame.LumaAt(Width / 2 - 12, y), 6, $"left of the edge, row {y}");
                    Assert.AreEqual(200, frame.LumaAt(Width / 2 + 12, y), 6, $"right of the edge, row {y}");
                    Assert.AreEqual(200, frame.LumaAt(Width - 3, y), 6, $"right border, row {y}");
                }

                Assert.AreEqual(90, frame.UAt(Width / 4, Height / 4), 6, "U");
                Assert.AreEqual(170, frame.VAt(Width / 4, Height / 4), 6, "V");
            }
        }

        [TestMethod, Timeout(120_000)]
        public void HevcSoftware_Matroska_Vfr_KeepsTimestamps()
        {
            long[] indices = { 0, 1, 15, 16, 100 };
            string path = PathFor("hevc-vfr.mkv");
            using (var recorder = new Recorder(path, "matroska", SoftwareVideo(Fps, variableFrameRate: true, codec: VideoCodec.Hevc), null))
            {
                recorder.Start();
                for (int i = 0; i < indices.Length; i++)
                    RecorderFeed.SubmitPattern(recorder, Width, Height, indices[i], i);
                recorder.Stop();
                Assert.IsTrue(recorder.CompletedSuccessfully);
            }

            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual("hevc", probe.VideoCodec);
            Assert.AreEqual(indices.Length, probe.VideoPacketCount);
            for (int i = 0; i < indices.Length; i++)
                Assert.AreEqual(indices[i] / (double)Fps, probe.VideoPts[i], 0.001, $"PTS of packet {i}");
        }

        [TestMethod, Timeout(120_000)]
        [DataRow(640, 360)]
        [DataRow(1280, 720)]   // BT.709 tagging threshold
        [DataRow(322, 242)]    // even, but not a multiple of 16
        public void OtherFrameSizes_AreRecordedAtTheirSize(int width, int height)
        {
            const int frames = 15;
            string path = PathFor($"size-{width}x{height}.mp4");
            using (var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps, width: width, height: height, bitrate: 2_000_000), null))
            {
                recorder.Start();
                for (int i = 0; i < frames; i++)
                    RecorderFeed.SubmitPattern(recorder, width, height, i, i);
                recorder.Stop();
                Assert.IsTrue(recorder.CompletedSuccessfully);
                Assert.AreEqual(0, recorder.DroppedFrames);
            }

            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(width, probe.Width);
            Assert.AreEqual(height, probe.Height);
            Assert.AreEqual(frames, probe.VideoPacketCount);

            DecodedVideoFrame first = MediaProbe.DecodeVideo(path, 1).Single();
            Assert.AreEqual(width, first.Width);
            Assert.AreEqual(height, first.Height);
        }
    }
}
