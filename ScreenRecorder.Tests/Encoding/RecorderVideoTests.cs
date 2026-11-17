using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Encoder;
using ScreenRecorder.Tests.Support;

namespace ScreenRecorder.Tests.Encoding
{
    /// <summary>
    /// The recorder's video path, driven with synthetic NV12 frames through the software H.264
    /// encoder into MP4 and verified by reading the file back with libavformat.
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    public sealed unsafe class RecorderVideoTests : RecorderTestBase
    {
        /// <summary>MP4 stores timestamps in a time scale that represents n/fps exactly.</summary>
        private const double PtsTolerance = 1e-6;

        private static void AssertPts(IReadOnlyList<long> expectedIndices, double secondsPerIndex, IReadOnlyList<double> actualSeconds, double tolerance = PtsTolerance)
        {
            Assert.AreEqual(expectedIndices.Count, actualSeconds.Count, "number of video packets");
            for (int i = 0; i < expectedIndices.Count; i++)
                Assert.AreEqual(expectedIndices[i] * secondsPerIndex, actualSeconds[i], tolerance, $"PTS of video packet {i}");
        }

        private static long[] Range(int count) => Enumerable.Range(0, count).Select(i => (long)i).ToArray();

        #region Constant frame rate

        [TestMethod, Timeout(120_000)]
        [DataRow(15, 30)]
        [DataRow(30, 90)]
        [DataRow(60, 120)]
        public void Cfr_SubmitVideoBuffer_WritesExactlyOnePacketPerGridIndex(int fps, int frames)
        {
            string path = PathFor("cfr.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(fps), null);
            recorder.Start();

            for (int i = 0; i < frames; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);

            Assert.AreEqual(frames, recorder.RecordedVideoFrames);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.IsNull(recorder.Fault);
            Assert.AreEqual(0, recorder.DroppedFrames);
            Assert.AreEqual(frames, recorder.RecordedVideoFrames);
            Assert.AreEqual(frames, recorder.EncodedVideoFrames);

            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(1, probe.StreamCount);
            Assert.AreEqual("h264", probe.VideoCodec);
            Assert.AreEqual(Width, probe.Width);
            Assert.AreEqual(Height, probe.Height);
            Assert.IsFalse(probe.HasAudio);

            AssertPts(Range(frames), 1.0 / fps, probe.VideoPts);
            AssertPts(Range(frames), 1.0 / fps, probe.VideoDts); // no B-frames: decode order == display order
            Assert.AreEqual(frames / (double)fps, probe.DurationSeconds, 0.5 / fps, "duration");
            Assert.AreEqual(fps, probe.AverageFrameRate, 0.01, "average frame rate");
            Assert.IsTrue(probe.VideoKeyFrames[0], "the first frame must be a keyframe");
        }

        [TestMethod, Timeout(120_000)]
        public void Cfr_ReportsTheEncoderInUse_AndTheBytesWritten()
        {
            string path = PathFor("info.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(30), null);

            Assert.IsTrue(recorder.HasVideo);
            Assert.IsFalse(recorder.HasAudio);
            Assert.IsNull(recorder.VideoEncoderName, "no encoder before Start");
            Assert.AreEqual(0, recorder.BytesWritten);

            recorder.Start();
            Assert.AreEqual("libx264", recorder.VideoEncoderName);
            Assert.AreEqual(HwAccel.Software, recorder.ActualHwAccel);
            Assert.IsFalse(recorder.UsesGpuInput);

            for (int i = 0; i < 30; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
            recorder.Stop();

            long fileLength = new FileInfo(path).Length;
            Assert.IsTrue(recorder.BytesWritten > 0);
            Assert.IsTrue(recorder.BytesWritten <= fileLength, $"BytesWritten {recorder.BytesWritten} exceeds the file size {fileLength}");
            Assert.AreEqual(HwAccel.Software, recorder.ActualHwAccel, "still readable after Stop");
        }

        [TestMethod, Timeout(120_000)]
        [DataRow("matroska", "mkv")]
        [DataRow("mpegts", "ts")]
        [DataRow("avi", "avi")]
        public void BytesWritten_AfterStop_IsTheFileSize(string format, string extension)
        {
            string path = PathFor("bytes." + extension);
            using var recorder = new Recorder(path, format, SoftwareVideo(30), null);
            recorder.Start();
            for (int i = 0; i < 30; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
            recorder.Stop();

            long fileLength = new FileInfo(path).Length;
            Assert.IsTrue(recorder.BytesWritten <= fileLength, $"BytesWritten {recorder.BytesWritten} exceeds the file size {fileLength}");
            Assert.IsTrue(recorder.BytesWritten >= fileLength * 0.9, $"BytesWritten {recorder.BytesWritten}, file size {fileLength}");
        }

        [TestMethod, Timeout(120_000)]
        [DataRow("mp4", "mp4")]
        [DataRow("mov", "mov")]
        public void BytesWritten_AfterStop_IsTheFileSize_MovFamily(string format, string extension)
        {
            string path = PathFor("bytes." + extension);
            using var recorder = new Recorder(path, format, SoftwareVideo(30), null);
            recorder.Start();
            for (int i = 0; i < 30; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
            recorder.Stop();

            long fileLength = new FileInfo(path).Length;
            Assert.IsTrue(recorder.BytesWritten <= fileLength, $"BytesWritten {recorder.BytesWritten} exceeds the file size {fileLength}");
            Assert.IsTrue(recorder.BytesWritten >= fileLength * 0.9, $"BytesWritten {recorder.BytesWritten}, file size {fileLength}");
        }

        [TestMethod, Timeout(120_000)]
        public void Cfr_FractionalFrameRate_KeepsExactNtscTimestamps()
        {
            const int frames = 60;
            string path = PathFor("ntsc.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(30000, fpsDenominator: 1001), null);
            recorder.Start();

            for (int i = 0; i < frames; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            MediaProbe probe = ProbeFile(path);
            AssertPts(Range(frames), 1001.0 / 30000, probe.VideoPts);
            Assert.AreEqual(frames * 1001.0 / 30000, probe.DurationSeconds, 0.02, "duration");
            Assert.AreEqual(29.97, probe.AverageFrameRate, 0.01);
        }

        [TestMethod, Timeout(120_000)]
        public void Cfr_LegacyPushVideoFrame_WritesExactlyOnePacketPerFrame()
        {
            const int fps = 30;
            const int frames = 75;
            string path = PathFor("legacy.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(fps), null);
            recorder.Start();

            int bytes = Width * Height * 3 / 2;
            nint own = Marshal.AllocHGlobal(bytes);
            try
            {
                long t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < frames; i++)
                {
                    // The same caller-owned buffer every time: the recorder must copy it.
                    Nv12TestPattern.Fill(own, Width, Width, Height, i);
                    long timestamp = t0 + i * Stopwatch.Frequency / fps;
                    recorder.PushVideoFrame(new RawVideoFrame(own, Width, Width, Height, PixelFormat.NV12, timestamp));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(own);
            }

            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.AreEqual(0, recorder.DroppedFrames);
            Assert.AreEqual(frames, recorder.RecordedVideoFrames);

            MediaProbe probe = ProbeFile(path);
            AssertPts(Range(frames), 1.0 / fps, probe.VideoPts);
            Assert.AreEqual(frames / (double)fps, probe.DurationSeconds, 0.5 / fps, "duration");
        }

        [TestMethod, Timeout(120_000)]
        public void Cfr_LegacyPushVideoFrame_DerivesTheGridIndexFromTheTimestamp()
        {
            const int fps = 30;
            string path = PathFor("legacy-ts.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(fps, variableFrameRate: true), null);
            recorder.Start();

            int bytes = Width * Height * 3 / 2;
            nint own = Marshal.AllocHGlobal(bytes);
            try
            {
                long t0 = 1_000_000_000;
                long period = Stopwatch.Frequency / fps;

                void Push(long timestamp, int pattern)
                {
                    Nv12TestPattern.Fill(own, Width, Width, Height, pattern);
                    recorder.PushVideoFrame(new RawVideoFrame(own, Width, Width, Height, PixelFormat.NV12, timestamp));
                }

                Push(t0, 0);                           // index 0 (the first frame defines the origin)
                Push(t0 + period, 1);                  // index 1
                Push(t0 + period + period / 10, 2);    // still rounds to index 1 → ignored
                Push(t0 + 2 * period - period / 10, 3);// rounds to index 2
                Push(t0 + 10 * period, 4);             // index 10 (a gap)
                Push(t0 + 5 * period, 5);              // goes back in time → ignored
            }
            finally
            {
                Marshal.FreeHGlobal(own);
            }

            recorder.Stop();

            Assert.AreEqual(4, recorder.RecordedVideoFrames);
            MediaProbe probe = ProbeFile(path);
            AssertPts(new long[] { 0, 1, 2, 10 }, 1.0 / fps, probe.VideoPts);
        }

        #endregion

        #region Variable frame rate

        [TestMethod, Timeout(120_000)]
        public void Vfr_SparseGridIndices_KeepTheirExactTimestamps()
        {
            const int fps = 30;
            long[] indices = { 0, 1, 2, 30, 31, 90, 150 };
            string path = PathFor("vfr.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(fps, variableFrameRate: true), null);
            recorder.Start();

            for (int i = 0; i < indices.Length; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, indices[i], i);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.AreEqual(0, recorder.DroppedFrames);
            Assert.AreEqual(indices.Length, recorder.RecordedVideoFrames);
            Assert.AreEqual(indices.Length, recorder.EncodedVideoFrames);

            MediaProbe probe = ProbeFile(path);
            AssertPts(indices, 1.0 / fps, probe.VideoPts);
            AssertPts(indices, 1.0 / fps, probe.VideoDts);

            // 7 frames spread over 5 s: the file must not claim the 30 fps grid rate.
            Assert.IsTrue(probe.AverageFrameRate < 5, $"average frame rate {probe.AverageFrameRate} for 7 frames in 5 s");
        }

        [TestMethod, Timeout(120_000)]
        public void Vfr_Duration_EndsOneGridTickAfterTheLastFrame()
        {
            // The last frame is at 150/30 = 5.0 s. Its own display time is one grid tick, so the
            // track must end at 5.0 s (+ at most one tick) — not later: the capture source stamps
            // its closing frame at the stop position precisely so that the video track is as
            // long as the recording (and as the audio track).
            const int fps = 30;
            long[] indices = { 0, 1, 2, 30, 31, 90, 150 };
            string path = PathFor("vfr-duration.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(fps, variableFrameRate: true), null);
            recorder.Start();

            for (int i = 0; i < indices.Length; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, indices[i], i);
            recorder.Stop();

            MediaProbe probe = ProbeFile(path);
            Assert.IsTrue(probe.DurationSeconds >= 5.0 - 1e-3 && probe.DurationSeconds <= 5.0 + 1.0 / fps + 1e-3,
                $"video track duration is {probe.DurationSeconds:0.###} s; the last frame is at 5.000 s");
        }

        [TestMethod, Timeout(120_000)]
        public void Vfr_Matroska_KeepsTimestampsToTheMillisecond()
        {
            const int fps = 60;
            long[] indices = { 0, 1, 2, 30, 31, 90, 150, 151, 600 };
            string path = PathFor("vfr.mkv");
            using var recorder = new Recorder(path, "matroska", SoftwareVideo(fps, variableFrameRate: true), null);
            recorder.Start();

            for (int i = 0; i < indices.Length; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, indices[i], i);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            MediaProbe probe = ProbeFile(path);
            AssertPts(indices, 1.0 / fps, probe.VideoPts, tolerance: 0.001); // Matroska's time base is 1 ms
        }

        #endregion

        #region Repeats

        [TestMethod, Timeout(120_000)]
        public void RepeatLastVideoFrame_WritesAPacketAtEachRepeatedIndex()
        {
            const int fps = 30;
            string path = PathFor("repeat.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(fps, variableFrameRate: true), null);
            recorder.Start();

            RecorderFeed.SubmitPattern(recorder, Width, Height, 0, 0);
            RecorderFeed.Repeat(recorder, 1);
            RecorderFeed.Repeat(recorder, 2);
            RecorderFeed.SubmitPattern(recorder, Width, Height, 3, 1);
            RecorderFeed.Repeat(recorder, 10);   // keep-alive after a gap
            RecorderFeed.Repeat(recorder, 45);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.AreEqual(0, recorder.DroppedFrames);
            Assert.AreEqual(6, recorder.RecordedVideoFrames, "repeats count as recorded frames");
            Assert.AreEqual(6, recorder.EncodedVideoFrames);

            MediaProbe probe = ProbeFile(path);
            AssertPts(new long[] { 0, 1, 2, 3, 10, 45 }, 1.0 / fps, probe.VideoPts);
        }

        [TestMethod, Timeout(120_000)]
        public void RepeatLastVideoFrame_ShowsThePreviousPicture()
        {
            const int fps = 30;
            string path = PathFor("repeat-picture.mp4");
            var video = SoftwareVideo(fps);
            video.RateControl = RateControl.Cq;
            video.Quality = 12;
            using var recorder = new Recorder(path, "mp4", video, null);
            recorder.Start();

            RecorderFeed.SubmitSplit(recorder, Width, Height, Width, 0, leftLuma: 40, rightLuma: 40);
            RecorderFeed.Repeat(recorder, 1);
            RecorderFeed.Repeat(recorder, 2);
            RecorderFeed.SubmitSplit(recorder, Width, Height, Width, 3, leftLuma: 210, rightLuma: 210);
            RecorderFeed.Repeat(recorder, 4);
            RecorderFeed.SubmitSplit(recorder, Width, Height, Width, 5, leftLuma: 120, rightLuma: 120);
            recorder.Stop();

            List<DecodedVideoFrame> decoded = MediaProbe.DecodeVideo(path);

            Assert.AreEqual(6, decoded.Count);
            double[] expectedLuma = { 40, 40, 40, 210, 210, 120 };
            for (int i = 0; i < decoded.Count; i++)
            {
                Assert.AreEqual(i / (double)fps, decoded[i].PtsSeconds, PtsTolerance, $"PTS of decoded frame {i}");
                Assert.AreEqual(expectedLuma[i], decoded[i].MeanLuma(), 3.0, $"mean luma of decoded frame {i}");
            }
        }

        [TestMethod, Timeout(120_000)]
        public void RepeatLastVideoFrame_BeforeAnyFrame_ReturnsFalse()
        {
            string path = PathFor("repeat-first.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(30), null);
            recorder.Start();

            bool accepted = recorder.RepeatLastVideoFrame(0);

            Assert.IsFalse(accepted, "there is no picture to repeat yet");
            Assert.AreEqual(0, recorder.RecordedVideoFrames, "a rejected repeat must not count as a recorded frame");

            // ...and it must not have claimed grid index 0: the first real frame still goes there.
            int bytes = Width * Height * 3 / 2;
            nint buffer = recorder.RentVideoBuffer(bytes);
            Assert.AreNotEqual((nint)0, buffer);
            Nv12TestPattern.Fill(buffer, Width, Width, Height, 0);
            Assert.IsTrue(recorder.SubmitVideoBuffer(buffer, Width, bytes, 0), "the first frame at index 0 was rejected after a premature repeat");
            recorder.Stop();

            MediaProbe probe = ProbeFile(path);
            AssertPts(new long[] { 0 }, 1.0 / 30, probe.VideoPts);
        }

        [TestMethod, Timeout(120_000)]
        public void RepeatLastVideoFrame_BeforeAnyFrame_WritesNoPacket()
        {
            // Whatever the return value: with no picture to repeat nothing may reach the file,
            // and frames submitted afterwards (at later indices) are recorded normally.
            const int fps = 30;
            string path = PathFor("repeat-first-file.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(fps, variableFrameRate: true), null);
            recorder.Start();

            bool accepted = recorder.RepeatLastVideoFrame(5);
            long neverEncoded = accepted ? 1 : 0;
            if (accepted)
                RecorderFeed.WaitUntil(() => recorder.DroppedFrames == 1, "the premature repeat to be discarded by the encoder thread");

            RecorderFeed.WaitForVideoCapacity(recorder, neverEncoded);
            int bytes = Width * Height * 3 / 2;
            foreach (long index in new long[] { 10, 11 })
            {
                RecorderFeed.WaitForVideoCapacity(recorder, neverEncoded);
                nint buffer = recorder.RentVideoBuffer(bytes);
                Assert.AreNotEqual((nint)0, buffer);
                Nv12TestPattern.Fill(buffer, Width, Width, Height, (int)index);
                Assert.IsTrue(recorder.SubmitVideoBuffer(buffer, Width, bytes, index));
            }

            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.AreEqual(2, recorder.EncodedVideoFrames);
            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(2, probe.VideoPacketCount, "the premature repeat produced a packet");
            Assert.AreEqual(1.0 / fps, probe.VideoPts[1] - probe.VideoPts[0], PtsTolerance);
        }

        [TestMethod, Timeout(120_000)]
        public void NonIncreasingGridIndex_IsRejected()
        {
            const int fps = 30;
            string path = PathFor("monotonic.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(fps, variableFrameRate: true), null);
            recorder.Start();
            int bytes = Width * Height * 3 / 2;

            bool SubmitRaw(long index)
            {
                RecorderFeed.WaitForVideoCapacity(recorder);
                nint buffer = recorder.RentVideoBuffer(bytes);
                Assert.AreNotEqual((nint)0, buffer);
                Nv12TestPattern.Fill(buffer, Width, Width, Height, (int)index);
                return recorder.SubmitVideoBuffer(buffer, Width, bytes, index);
            }

            Assert.IsTrue(SubmitRaw(0));
            Assert.IsTrue(SubmitRaw(5));

            Assert.IsFalse(recorder.RepeatLastVideoFrame(5), "repeat at the same index");
            Assert.IsFalse(recorder.RepeatLastVideoFrame(4), "repeat at an earlier index");
            Assert.IsFalse(SubmitRaw(5), "new frame at the same index");
            Assert.IsFalse(SubmitRaw(3), "new frame at an earlier index");
            Assert.IsFalse(SubmitRaw(-1), "negative index");

            Assert.IsTrue(recorder.RepeatLastVideoFrame(6));
            Assert.IsFalse(recorder.RepeatLastVideoFrame(6), "repeat at an index that already has a repeat");
            Assert.IsTrue(SubmitRaw(7));
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.AreEqual(4, recorder.RecordedVideoFrames);
            Assert.AreEqual(3, recorder.DroppedFrames, "each rejected new frame is counted as dropped (rejected repeats are not)");

            MediaProbe probe = ProbeFile(path);
            AssertPts(new long[] { 0, 5, 6, 7 }, 1.0 / fps, probe.VideoPts);
            for (int i = 1; i < probe.VideoPts.Count; i++)
                Assert.IsTrue(probe.VideoPts[i] > probe.VideoPts[i - 1], "timestamps in the file must be strictly increasing");
        }

        #endregion

        #region Keyframes

        [TestMethod, Timeout(120_000)]
        public void Keyframes_Cfr_AreAtMostTwoSecondsApart()
        {
            const int fps = 30;
            const int frames = 150; // 5 s
            string path = PathFor("keys-cfr.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(fps), null);
            recorder.Start();

            for (int i = 0; i < frames; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
            recorder.Stop();

            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(frames, probe.VideoPacketCount);
            Assert.IsTrue(probe.VideoKeyFrames[0], "first frame");

            var keyTimes = Enumerable.Range(0, frames).Where(i => probe.VideoKeyFrames[i]).Select(i => probe.VideoPts[i]).ToList();
            Assert.IsTrue(keyTimes.Count >= 3, $"only {keyTimes.Count} keyframes in 5 s");
            for (int i = 1; i < keyTimes.Count; i++)
                Assert.IsTrue(keyTimes[i] - keyTimes[i - 1] <= 2.0 + PtsTolerance, $"keyframes {keyTimes[i - 1]:0.###} s and {keyTimes[i]:0.###} s are more than 2 s apart");
            Assert.IsTrue(probe.VideoPts[frames - 1] - keyTimes[keyTimes.Count - 1] < 2.0, "the tail of the recording has no keyframe within 2 s");

            // The forced keyframes sit exactly on the 2 s grid (frames 0, 60, 120).
            foreach (int index in new[] { 0, 60, 120 })
                Assert.IsTrue(probe.VideoKeyFrames[index], $"frame {index} ({index / (double)fps:0.#} s) is not a keyframe");
        }

        [TestMethod, Timeout(120_000)]
        public void Keyframes_Vfr_EveryFrameTwoSecondsAfterTheLastKeyframeIsAKeyframe()
        {
            const int fps = 30;
            // Sparse frames. Expected forced keyframes (≥ 60 grid ticks after the previous one):
            //   0 (first), 70 (2.33 s later), 200, 260 (exactly 2 s after 200), 400.
            // Not forced: 10, 75, 259 (1.97 s after 200).
            long[] indices = { 0, 10, 70, 75, 200, 259, 260, 400 };
            string path = PathFor("keys-vfr.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(fps, variableFrameRate: true), null);
            recorder.Start();

            for (int i = 0; i < indices.Length; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, indices[i], i); // slowly changing picture
            recorder.Stop();

            MediaProbe probe = ProbeFile(path);
            AssertPts(indices, 1.0 / fps, probe.VideoPts);

            // The rule, whatever the encoder decides on its own.
            Assert.IsTrue(probe.VideoKeyFrames[0], "first frame");
            double lastKey = probe.VideoPts[0];
            for (int i = 1; i < indices.Length; i++)
            {
                if (probe.VideoPts[i] - lastKey >= 2.0 - PtsTolerance)
                    Assert.IsTrue(probe.VideoKeyFrames[i], $"frame at index {indices[i]} is {probe.VideoPts[i] - lastKey:0.###} s after the last keyframe but is not a keyframe");
                if (probe.VideoKeyFrames[i])
                    lastKey = probe.VideoPts[i];
            }

            // And the exact pattern for this (smoothly changing) content.
            var keyIndices = Enumerable.Range(0, indices.Length).Where(i => probe.VideoKeyFrames[i]).Select(i => indices[i]).ToArray();
            CollectionAssert.AreEqual(new long[] { 0, 70, 200, 260, 400 }, keyIndices,
                "keyframes at " + string.Join(", ", keyIndices));
        }

        [TestMethod, Timeout(120_000)]
        public void Keyframes_Vfr_RepeatAfterALongStaticPeriod_IsAKeyframe()
        {
            // A static screen: one picture, then only keep-alive repeats one second apart. Every
            // other repeat is 2 s after a keyframe and must itself be one (seek points, and the
            // MP4 fragments that make an interrupted recording playable).
            const int fps = 30;
            string path = PathFor("keys-repeat.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(fps, variableFrameRate: true), null);
            recorder.Start();

            RecorderFeed.SubmitPattern(recorder, Width, Height, 0, 0);
            for (int second = 1; second <= 6; second++)
                RecorderFeed.Repeat(recorder, second * fps);
            recorder.Stop();

            MediaProbe probe = ProbeFile(path);
            AssertPts(new long[] { 0, 30, 60, 90, 120, 150, 180 }, 1.0 / fps, probe.VideoPts);
            CollectionAssert.AreEqual(new[] { true, false, true, false, true, false, true }, probe.VideoKeyFrames);
        }

        #endregion

        #region Picture integrity

        [TestMethod, Timeout(120_000)]
        [DataRow(Width)]        // tightly packed
        [DataRow(Width + 64)]   // padded rows, like a D3D staging texture's RowPitch
        [DataRow(Width + 2)]
        public void SubmitVideoBuffer_AnyStride_DecodesToTheSubmittedPicture(int stride)
        {
            string path = PathFor("stride.mp4");
            var video = SoftwareVideo(30);
            video.RateControl = RateControl.Cq;
            video.Quality = 10;
            using var recorder = new Recorder(path, "mp4", video, null);
            recorder.Start();

            const byte left = 60, right = 200, u = 90, v = 170;
            RecorderFeed.SubmitSplit(recorder, Width, Height, stride, 0, left, right, u, v);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            DecodedVideoFrame frame = MediaProbe.DecodeVideo(path, 1).Single();
            Assert.AreEqual(Width, frame.Width);
            Assert.AreEqual(Height, frame.Height);

            // Luma: flat halves, sharp vertical edge at the centre on EVERY row (a stride mix-up
            // would shear the edge), and no padding bytes (0xFF) leaking in at the right border.
            foreach (int y in new[] { 2, Height / 2, Height - 3 })
            {
                Assert.AreEqual(left, frame.LumaAt(8, y), 6, $"left half, row {y}");
                Assert.AreEqual(left, frame.LumaAt(Width / 2 - 12, y), 6, $"left of the edge, row {y}");
                Assert.AreEqual(right, frame.LumaAt(Width / 2 + 12, y), 6, $"right of the edge, row {y}");
                Assert.AreEqual(right, frame.LumaAt(Width - 3, y), 6, $"right border, row {y}");
            }

            // Chroma: U and V are not swapped and come from the right plane offset.
            foreach (int cy in new[] { 1, Height / 4, Height / 2 - 2 })
            {
                foreach (int cx in new[] { 2, Width / 4, Width / 2 - 2 })
                {
                    Assert.AreEqual(u, frame.UAt(cx, cy), 6, $"U at chroma ({cx},{cy})");
                    Assert.AreEqual(v, frame.VAt(cx, cy), 6, $"V at chroma ({cx},{cy})");
                }
            }
        }

        [TestMethod, Timeout(120_000)]
        public void SubmitVideoBuffer_FramesAreEncodedInSubmissionOrder()
        {
            // Each frame has its own flat brightness; the decoded sequence must match.
            const int fps = 30;
            byte[] levels = { 30, 90, 150, 210, 60, 180, 120, 240 };
            string path = PathFor("order.mp4");
            var video = SoftwareVideo(fps);
            video.RateControl = RateControl.Cq;
            video.Quality = 12;
            using var recorder = new Recorder(path, "mp4", video, null);
            recorder.Start();

            for (int i = 0; i < levels.Length; i++)
                RecorderFeed.SubmitSplit(recorder, Width, Height, Width, i, levels[i], levels[i]);
            recorder.Stop();

            List<DecodedVideoFrame> decoded = MediaProbe.DecodeVideo(path);
            Assert.AreEqual(levels.Length, decoded.Count);
            for (int i = 0; i < levels.Length; i++)
                Assert.AreEqual(levels[i], decoded[i].MeanLuma(), 3.0, $"decoded frame {i}");
        }

        #endregion

        #region Rate control

        [TestMethod, Timeout(120_000)]
        public void ConstantQuality_ProducesAValidFile()
        {
            const int fps = 30;
            const int frames = 45;
            string path = PathFor("cq.mp4");
            var video = SoftwareVideo(fps);
            video.RateControl = RateControl.Cq;
            video.Quality = 23;
            video.Bitrate = 0;
            using var recorder = new Recorder(path, "mp4", video, null);
            recorder.Start();

            for (int i = 0; i < frames; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.AreEqual(0, recorder.DroppedFrames);
            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual("h264", probe.VideoCodec);
            AssertPts(Range(frames), 1.0 / fps, probe.VideoPts);
            Assert.AreEqual(frames, MediaProbe.DecodeVideo(path).Count, "every packet must decode");
        }

        [TestMethod, Timeout(120_000)]
        public void ConstantQuality_LowerQualityValue_SpendsMoreBits()
        {
            long Record(int quality)
            {
                string path = PathFor($"cq-{quality}.mp4");
                var video = SoftwareVideo(30);
                video.RateControl = RateControl.Cq;
                video.Quality = quality;
                using var recorder = new Recorder(path, "mp4", video, null);
                recorder.Start();
                for (int i = 0; i < 30; i++)
                    RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
                recorder.Stop();
                Assert.IsTrue(recorder.CompletedSuccessfully);
                Assert.AreEqual(30, ProbeFile(path).VideoPacketCount);
                return new FileInfo(path).Length;
            }

            long high = Record(16);
            long low = Record(40);

            Assert.IsTrue(high > low * 1.5, $"quality 16 → {high} bytes, quality 40 → {low} bytes: the quality value is not applied");
        }

        [TestMethod, Timeout(120_000)]
        public void ConstantBitrate_HigherBitrate_ProducesALargerFile()
        {
            long Record(int bitrate)
            {
                string path = PathFor($"cbr-{bitrate}.mp4");
                using var recorder = new Recorder(path, "mp4", SoftwareVideo(30, bitrate: bitrate), null);
                recorder.Start();
                for (int i = 0; i < 90; i++)
                    RecorderFeed.SubmitNoise(recorder, Width, Height, i, seed: i); // incompressible: rate control decides the size
                recorder.Stop();
                Assert.IsTrue(recorder.CompletedSuccessfully);
                Assert.AreEqual(90, ProbeFile(path).VideoPacketCount);
                return new FileInfo(path).Length;
            }

            long small = Record(500_000);
            long large = Record(3_000_000);

            // 3 s of video: about 190 kB vs 1.1 MB.
            Assert.IsTrue(large > small * 2, $"500 kbps → {small} bytes, 3 Mbps → {large} bytes");
            Assert.AreEqual(3_000_000 * 3 / 8.0, large, 3_000_000 * 3 / 8.0 * 0.5, "3 Mbps for 3 s");
            Assert.IsTrue(small < 500_000 * 3 / 8.0 * 2, $"500 kbps for 3 s produced {small} bytes");
        }

        #endregion

        #region Lifecycle

        [TestMethod, Timeout(120_000)]
        public void BeforeStart_VideoInputIsRejected()
        {
            using var recorder = new Recorder(PathFor("not-started.mp4"), "mp4", SoftwareVideo(30), null);

            Assert.AreEqual((nint)0, recorder.RentVideoBuffer(Width * Height * 3 / 2));
            Assert.IsFalse(recorder.RepeatLastVideoFrame(0));
            Assert.AreEqual(0, recorder.RecordedVideoFrames);
            Assert.IsFalse(File.Exists(PathFor("not-started.mp4")), "the file is only created by Start");
        }

        [TestMethod, Timeout(120_000)]
        public void AfterStop_VideoInputIsRejected_AndTheFileIsUntouched()
        {
            string path = PathFor("stopped.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(30), null);
            recorder.Start();
            for (int i = 0; i < 10; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
            recorder.Stop();
            long length = new FileInfo(path).Length;

            Assert.AreEqual((nint)0, recorder.RentVideoBuffer(Width * Height * 3 / 2));
            Assert.IsFalse(recorder.RepeatLastVideoFrame(10));

            nint own = Marshal.AllocHGlobal(Width * Height * 3 / 2);
            try
            {
                Nv12TestPattern.Fill(own, Width, Width, Height, 0);
                recorder.PushVideoFrame(new RawVideoFrame(own, Width, Width, Height, PixelFormat.NV12, Stopwatch.GetTimestamp()));
            }
            finally
            {
                Marshal.FreeHGlobal(own);
            }

            recorder.Stop(); // second Stop is a no-op
            recorder.Start(); // a recorder is single-use: Start after Stop is a no-op, too

            Assert.AreEqual(10, recorder.RecordedVideoFrames);
            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.AreEqual(length, new FileInfo(path).Length);
            Assert.AreEqual(10, ProbeFile(path).VideoPacketCount);
        }

        [TestMethod, Timeout(120_000)]
        public void Start_Twice_IsANoOp()
        {
            string path = PathFor("start-twice.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(30), null);
            recorder.Start();
            RecorderFeed.SubmitPattern(recorder, Width, Height, 0, 0);

            recorder.Start();

            RecorderFeed.SubmitPattern(recorder, Width, Height, 1, 1);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.AreEqual(2, ProbeFile(path).VideoPacketCount);
        }

        [TestMethod, Timeout(120_000)]
        public void ReturnVideoBuffer_GivesTheBufferBackWithoutRecordingAFrame()
        {
            string path = PathFor("returned.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(30), null);
            recorder.Start();
            int bytes = Width * Height * 3 / 2;

            nint first = recorder.RentVideoBuffer(bytes);
            Assert.AreNotEqual((nint)0, first);
            recorder.ReturnVideoBuffer(first);

            nint second = recorder.RentVideoBuffer(bytes);
            Assert.AreEqual(first, second, "a returned buffer is reused");
            recorder.ReturnVideoBuffer(second);

            Assert.AreEqual(0, recorder.RecordedVideoFrames);
            Assert.AreEqual(0, recorder.DroppedFrames);

            RecorderFeed.SubmitPattern(recorder, Width, Height, 0, 0);
            recorder.Stop();

            Assert.AreEqual(1, ProbeFile(path).VideoPacketCount);
        }

        #endregion
    }
}
