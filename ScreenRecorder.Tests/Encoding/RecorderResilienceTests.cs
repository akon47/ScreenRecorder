using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Encoder;
using ScreenRecorder.Tests.Support;

namespace ScreenRecorder.Tests.Encoding
{
    /// <summary>What the recorder does when things go wrong, or when it is used at the edges.</summary>
    [TestClass]
    [TestCategory("Integration")]
    public sealed unsafe class RecorderResilienceTests : RecorderTestBase
    {
        private const int Fps = 30;
        private const int Block = 1024;
        private const int SampleRate = 48000;

        /// <summary>Runs an action that must return on its own; fails instead of hanging the suite.</summary>
        private static void MustReturn(Action action, string what, int timeoutMs = 30_000)
        {
            Task task = Task.Run(action);
            if (!task.Wait(timeoutMs))
                Assert.Fail($"{what} did not return within {timeoutMs / 1000} s.");
        }

        #region Constructor

        [TestMethod]
        public void Constructor_NullUrl_Throws()
        {
            var ex = Assert.ThrowsExactly<ArgumentNullException>(() => new Recorder(null, "mp4", SoftwareVideo(Fps), null));
            Assert.AreEqual("url", ex.ParamName);
        }

        [TestMethod]
        public void Constructor_NullFormat_Throws()
        {
            var ex = Assert.ThrowsExactly<ArgumentNullException>(() => new Recorder(PathFor("x.mp4"), null, SoftwareVideo(Fps), null));
            Assert.AreEqual("formatShortName", ex.ParamName);
        }

        [TestMethod]
        public void Constructor_NeitherVideoNorAudio_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() => new Recorder(PathFor("x.mp4"), "mp4", null, null));
        }

        [TestMethod]
        public void Constructor_DoesNotTouchTheFileSystem()
        {
            string path = PathFor("untouched.mp4");

            using (var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps), StereoAudio()))
            {
                Assert.IsFalse(recorder.CompletedSuccessfully);
                Assert.IsNull(recorder.Fault);
                Assert.AreEqual(0, recorder.DroppedFrames);
                Assert.AreEqual(0, recorder.RecordedVideoFrames);
                Assert.AreEqual(0, recorder.EncodedVideoFrames);
                Assert.AreEqual(0, recorder.BytesWritten);
                Assert.IsFalse(recorder.UsesGpuInput);
            }

            Assert.IsFalse(File.Exists(path));
        }

        #endregion

        #region Start failures

        [TestMethod, Timeout(120_000)]
        public void Start_OutputDirectoryMissing_ThrowsAndLeavesNothingRunning()
        {
            string path = Path.Combine(TempDir, "no-such-directory", "out.mp4");
            var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps), StereoAudio());
            int faulted = 0;
            recorder.Faulted += _ => Interlocked.Increment(ref faulted);

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => recorder.Start());
            StringAssert.Contains(ex.Message, path, "the error should name the path that could not be opened");

            // Nothing is running: every input is refused, and Stop/Dispose return immediately.
            Assert.AreEqual((nint)0, recorder.RentVideoBuffer(Width * Height * 3 / 2));
            Assert.IsFalse(recorder.RepeatLastVideoFrame(0));
            RecorderFeed.PushSineS16(recorder, new SinePcm(SampleRate, 2), 4);
            Assert.AreEqual(0, recorder.RecordedVideoFrames);
            Assert.IsNull(recorder.VideoEncoderName, "the encoder opened before the failure must have been released");

            MustReturn(recorder.Stop, "Stop after a failed Start");
            MustReturn(recorder.Dispose, "Dispose after a failed Start");

            Assert.IsFalse(recorder.CompletedSuccessfully);
            Assert.IsFalse(File.Exists(path));
            Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(path)), "the recorder must not create directories");
            Assert.AreEqual(0, faulted, "a synchronous Start failure is reported by the exception, not by Faulted");
        }

        [TestMethod, Timeout(120_000)]
        public void Start_OutputDirectoryMissing_AudioOnly_Throws()
        {
            string path = Path.Combine(TempDir, "no-such-directory", "out.m4a");
            using var recorder = new Recorder(path, "ipod", null, StereoAudio());

            Assert.ThrowsExactly<InvalidOperationException>(() => recorder.Start());

            MustReturn(recorder.Stop, "Stop after a failed Start");
            Assert.IsFalse(recorder.CompletedSuccessfully);
            Assert.IsFalse(File.Exists(path));
        }

        [TestMethod, Timeout(120_000)]
        public void Start_UnknownContainerFormat_Throws()
        {
            string path = PathFor("out.xyz");
            using var recorder = new Recorder(path, "no-such-format", SoftwareVideo(Fps), null);

            Assert.ThrowsExactly<NotSupportedException>(() => recorder.Start());

            MustReturn(recorder.Stop, "Stop after a failed Start");
            Assert.IsFalse(recorder.CompletedSuccessfully);
            Assert.IsFalse(File.Exists(path));
        }

        [TestMethod, Timeout(120_000)]
        public void Start_UnsupportedVideoCodec_Throws()
        {
            string path = PathFor("bad-codec.mp4");
            var video = SoftwareVideo(Fps);
            video.Codec = VideoCodec.None;
            using var recorder = new Recorder(path, "mp4", video, null);

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => recorder.Start());

            MustReturn(recorder.Stop, "Stop after a failed Start");
            Assert.IsFalse(File.Exists(path), "no file may be left behind when the encoder could not be created");
        }

        [TestMethod, Timeout(120_000)]
        public void Start_UnsupportedAudioCodec_Throws()
        {
            string path = PathFor("bad-audio-codec.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps), StereoAudio(AudioCodec.None));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => recorder.Start());

            MustReturn(recorder.Stop, "Stop after a failed Start");
            Assert.IsFalse(File.Exists(path), "no file may be left behind when the encoder could not be created");
        }

        [TestMethod, Timeout(120_000)]
        public void Start_OutputFileLockedByAnotherProcess_Throws()
        {
            string path = PathFor("locked.mp4");
            using (new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                using var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps), null);

                Assert.ThrowsExactly<InvalidOperationException>(() => recorder.Start());
                MustReturn(recorder.Stop, "Stop after a failed Start");
                Assert.IsFalse(recorder.CompletedSuccessfully);
            }
        }

        [TestMethod, Timeout(120_000)]
        public void Start_ExistingFile_IsOverwritten()
        {
            string path = PathFor("existing.mp4");
            File.WriteAllBytes(path, new byte[2_000_000]); // 2 MB of zeros from a "previous recording"

            using (var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps), null))
            {
                recorder.Start();
                for (int i = 0; i < 10; i++)
                    RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
                recorder.Stop();
                Assert.IsTrue(recorder.CompletedSuccessfully);
            }

            Assert.IsTrue(new FileInfo(path).Length < 2_000_000, "stale bytes of the old file were kept");
            Assert.AreEqual(10, ProbeFile(path).VideoPacketCount);
        }

        #endregion

        #region Stop / Dispose

        [TestMethod, Timeout(120_000)]
        public void Stop_WithoutStart_DoesNothing()
        {
            string path = PathFor("never-started.mp4");
            var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps), StereoAudio());

            MustReturn(recorder.Stop, "Stop without Start");
            MustReturn(recorder.Dispose, "Dispose without Start");
            MustReturn(recorder.Dispose, "second Dispose");

            Assert.IsFalse(recorder.CompletedSuccessfully);
            Assert.IsFalse(File.Exists(path));
        }

        /// <summary>Starts a recorder and stops it again without a single frame or audio block.</summary>
        private void StartAndStopEmpty(string fileName, string format, VideoParams video, AudioParams audio)
        {
            string path = PathFor(fileName);
            var recorder = new Recorder(path, format, video, audio);
            recorder.Start();

            MustReturn(recorder.Stop, $"Stop of an empty {format} recording");

            Assert.IsNull(recorder.Fault, recorder.Fault?.ToString());
            Assert.AreEqual(0, recorder.RecordedVideoFrames);
            Assert.AreEqual(0, recorder.DroppedFrames);
            Assert.IsTrue(recorder.IsEmpty, "nothing was recorded");
            Assert.IsFalse(recorder.CompletedSuccessfully, "an empty recording is never a successful one");
            TestContext.WriteLine($"{format}: CompletedSuccessfully = {recorder.CompletedSuccessfully}, file size = {new FileInfo(path).Length}");

            MustReturn(recorder.Dispose, "Dispose");
            MustReturn(recorder.Dispose, "second Dispose");

            // The file handle is released: the file can be deleted right away.
            File.Delete(path);
            Assert.IsFalse(File.Exists(path));
        }

        [TestMethod, Timeout(120_000)]
        public void Stop_AfterOneFrame_IsNotEmpty()
        {
            string path = PathFor("one-frame.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps), null);
            recorder.Start();
            RecorderFeed.SubmitPattern(recorder, Width, Height, 0, 0);
            recorder.Stop();

            Assert.IsFalse(recorder.IsEmpty);
            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.AreEqual(new FileInfo(path).Length, recorder.BytesWritten, "after Stop this is the file size");
        }

        [TestMethod, Timeout(120_000)]
        [DataRow("matroska", "mkv")]
        [DataRow("mpegts", "ts")]
        [DataRow("avi", "avi")]
        public void Stop_WithoutAnyFrame_DoesNotHang(string format, string extension)
        {
            StartAndStopEmpty("empty." + extension, format, SoftwareVideo(Fps), StereoAudio());
        }

        [TestMethod, Timeout(120_000)]
        public void Stop_WithoutAnyFrame_Mp3AudioOnly_DoesNotHang()
        {
            StartAndStopEmpty("empty.mp3", "mp3", null, StereoAudio(AudioCodec.Mp3));
        }

        // NOT a hang but a hard crash: with the MOV-family muxers (mp4 / mov / ipod, all written
        // "hybrid fragmented") Recorder.Stop() -> FFmpegFileContainer.Complete() ->
        // av_write_trailer() dies with an access violation (0xC0000005) when not a single packet
        // was written. The exception cannot be caught; it takes the whole process (here: the
        // test host, in production: the application) down. One packet in either stream is
        // enough to avoid it (see the two tests below).
        private const string EmptyMovCrash =
            "suspected bug: Recorder.Stop() crashes the process (access violation 0xC0000005 inside av_write_trailer, " +
            "FFmpegFileContainer.Complete) when a mp4/mov/ipod recording is stopped before any packet was written. " +
            "Cannot run: it kills the test host.";

        [TestMethod, Timeout(120_000)]
        [DataRow("mp4", "mp4")]
        [DataRow("mov", "mov")]
        public void Stop_WithoutAnyFrame_MovFamily_DoesNotCrash(string format, string extension)
        {
            StartAndStopEmpty("empty." + extension, format, SoftwareVideo(Fps), StereoAudio());
        }

        [TestMethod, Timeout(120_000)]
        public void Stop_WithoutAnyFrame_Mp4VideoOnly_DoesNotCrash()
        {
            // E.g. recording a minimized window without audio: capture never delivers a frame.
            StartAndStopEmpty("empty-video-only.mp4", "mp4", SoftwareVideo(Fps), null);
        }

        [TestMethod, Timeout(120_000)]
        public void Stop_WithoutAnyFrame_M4aAudioOnly_DoesNotCrash()
        {
            StartAndStopEmpty("empty.m4a", "ipod", null, StereoAudio());
        }

        [TestMethod, Timeout(120_000)]
        [DataRow("mp4", "mp4")]
        [DataRow("mov", "mov")]
        public void Stop_MovFamily_NoVideoFrameButSomeAudio_FinalizesAnAudioOnlyTimeline(string format, string extension)
        {
            // Stop pressed before the first picture arrived: only audio was recorded.
            string path = PathFor("no-video." + extension);
            using var recorder = new Recorder(path, format, SoftwareVideo(Fps), StereoAudio());
            recorder.Start();
            RecorderFeed.PushSineS16(recorder, new SinePcm(SampleRate, 2), 3);

            MustReturn(recorder.Stop, "Stop");

            Assert.IsTrue(recorder.CompletedSuccessfully);
            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(2, probe.StreamCount);
            Assert.AreEqual(0, probe.VideoPacketCount);
            Assert.IsTrue(probe.AudioPacketCount >= 3, $"{probe.AudioPacketCount} audio packets");
        }

        [TestMethod, Timeout(120_000)]
        [DataRow("mp4", "mp4")]
        [DataRow("mov", "mov")]
        public void Stop_MovFamily_OneVideoFrameAndNoAudio_Finalizes(string format, string extension)
        {
            string path = PathFor("one-frame." + extension);
            using var recorder = new Recorder(path, format, SoftwareVideo(Fps), StereoAudio());
            recorder.Start();
            RecorderFeed.SubmitPattern(recorder, Width, Height, 0, 0);

            MustReturn(recorder.Stop, "Stop");

            Assert.IsTrue(recorder.CompletedSuccessfully);
            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(2, probe.StreamCount);
            Assert.AreEqual(1, probe.VideoPacketCount);
            Assert.AreEqual(0, probe.AudioPacketCount);
            Assert.AreEqual(1, MediaProbe.DecodeVideo(path).Count, "the single frame must decode");
        }

        [TestMethod, Timeout(120_000)]
        public void Dispose_IsIdempotent_AndReleasesTheFile()
        {
            string path = PathFor("disposed.mp4");
            var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps), null);
            recorder.Start();
            for (int i = 0; i < 5; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
            recorder.Stop();

            recorder.Dispose();
            recorder.Dispose();
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.AreEqual(5, recorder.RecordedVideoFrames, "counters stay readable after Dispose");

            // No handle left: the file can be opened exclusively.
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }
        }

        [TestMethod, Timeout(120_000)]
        public void Dispose_WithoutStop_FinalizesTheFile()
        {
            const int frames = 20;
            string path = PathFor("dispose-only.mp4");
            var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps), null);
            recorder.Start();
            for (int i = 0; i < frames; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);

            MustReturn(recorder.Dispose, "Dispose of a running recorder");

            Assert.IsTrue(recorder.CompletedSuccessfully);
            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(frames, probe.VideoPacketCount, "frames queued before Dispose must not be lost");
            Assert.AreEqual(frames / (double)Fps, probe.DurationSeconds, 0.02);
        }

        [TestMethod, Timeout(120_000)]
        public void Stop_DrainsEverythingThatWasAccepted()
        {
            // Stop immediately after a burst, without waiting for the encoder: every accepted
            // frame (new pictures and repeats alike) must still reach the file.
            string path = PathFor("drain.mp4");
            var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps), null);
            recorder.Start();

            RecorderFeed.SubmitPattern(recorder, Width, Height, 0, 0);
            long accepted = 1;
            for (int i = 1; i <= 50; i++)
            {
                if (recorder.RepeatLastVideoFrame(i))
                    accepted++;
            }

            MustReturn(recorder.Stop, "Stop");
            recorder.Dispose();

            Assert.AreEqual(51, accepted, "the input queue holds 64 entries: none of the 50 repeats may be refused");
            Assert.AreEqual(accepted, recorder.EncodedVideoFrames);
            Assert.AreEqual(accepted, ProbeFile(path).VideoPacketCount);
        }

        #endregion

        #region Crash resilience

        // What a crash / power loss leaves on disk is the file as it is in the middle of a
        // recording. The muxer writes "hybrid fragmented" MP4 (one fragment per video keyframe,
        // i.e. every 2 s), so everything up to the last completed fragment is meant to be
        // readable. The tests take a copy of the file while the recorder is still running and
        // read it back with libavformat.

        private const string UnflushedFragments =
            "suspected bug: finished MP4 fragments are not flushed to disk. FFmpegFileContainer never calls avio_flush and " +
            "libavformat buffers file output in 256 KiB blocks, so data reaches the disk only in 256 KiB steps. Until the " +
            "recording has produced its first 256 KiB the file on disk is an 88-byte stub without a moov atom (not openable " +
            "at all); after that the newest (up to) 256 KiB are always missing. Small / low-bitrate recordings (a small " +
            "region, constant quality with a static screen, audio-only M4A) are therefore unrecoverable for a long time.";

        /// <summary>
        /// Frame 0 at 0 and one frame period between all later frames. With AAC audio in MP4 the
        /// first frame is held up to 21.3 ms longer (both streams are shifted by the 1024 priming
        /// samples of the AAC encoder; see RecorderContainerTests).
        /// </summary>
        private static void AssertVideoTimeline(MediaProbe probe, string what)
        {
            Assert.AreEqual(0.0, probe.VideoPts[0], 1e-6, $"PTS of the first {what} video packet");
            double firstGap = probe.VideoPts[1] - probe.VideoPts[0];
            Assert.IsTrue(firstGap >= 1.0 / Fps - 1e-6 && firstGap <= 1.0 / Fps + (double)Block / SampleRate + 1e-4,
                $"first {what} video frame lasts {firstGap * 1000:0.0} ms");
            for (int i = 2; i < probe.VideoPacketCount; i++)
                Assert.AreEqual(1.0 / Fps, probe.VideoPts[i] - probe.VideoPts[i - 1], 1e-6, $"spacing before {what} video packet {i}");
        }

        /// <summary>
        /// Records <paramref name="frames"/> frames (+ matching audio) WITHOUT stopping, waits
        /// until a snapshot of the file contains at least <paramref name="minimumVideoPackets"/>
        /// readable video packets, and returns that snapshot's contents.
        /// </summary>
        private MediaProbe SnapshotWhileRecording(Recorder recorder, string path, string snapshot, int frames, bool noise, int minimumVideoPackets, int timeoutMs)
        {
            int audioBlocks = (int)(frames * (long)SampleRate / Fps / Block);
            var sine = new SinePcm(SampleRate, 2);
            int pushed = 0;
            for (int i = 0; i < frames; i++)
            {
                if (noise)
                    RecorderFeed.SubmitNoise(recorder, Width, Height, i, seed: i);
                else
                    RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);

                int due = Math.Min(audioBlocks, (int)((i + 1) * (long)SampleRate / Fps / Block));
                RecorderFeed.PushSineS16(recorder, sine, due - pushed);
                pushed = due;
            }

            RecorderFeed.WaitForVideoEncoded(recorder);

            MediaProbe partial = null;
            RecorderFeed.WaitUntil(() =>
            {
                TestFiles.CopyShared(path, snapshot);
                if (new FileInfo(snapshot).Length < 1024)
                    return false; // nothing but the file header yet: not worth probing
                partial = MediaProbe.Probe(snapshot);
                return partial.Opened && partial.VideoPacketCount >= minimumVideoPackets;
            }, $"{minimumVideoPackets} video packets to become readable from the file on disk (it has {new FileInfo(path).Length} bytes)", timeoutMs);

            Assert.IsNull(recorder.Fault, recorder.Fault?.ToString());
            return partial;
        }

        [TestMethod, Timeout(120_000)]
        public void Mp4_WhileStillRecording_ACopyOfTheFileIsAlreadyPlayable()
        {
            // 3 s at 30 fps: keyframes at 0 and 2 s, so the first fragment (2 s of video and
            // audio, about 250 kB at 1 Mbps) is complete as soon as frame 60 has been muxed.
            // Expected: at least 1 s of it is readable from disk. Actual: the file on disk stays
            // at 88 bytes ("moov atom not found") until Stop.
            const int frames = 90;
            string path = PathFor("live.mp4");
            string snapshot = PathFor("snapshot-after-crash.mp4");

            var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps), StereoAudio());
            try
            {
                recorder.Start();
                MediaProbe partial = SnapshotWhileRecording(recorder, path, snapshot, frames, noise: false, minimumVideoPackets: Fps, timeoutMs: 5_000);

                Assert.AreEqual(2, partial.StreamCount);
                Assert.IsTrue(partial.VideoPacketCount >= Fps, $"only {partial.VideoPacketCount} video packets are recoverable");
                Assert.IsTrue(partial.VideoKeyFrames[0]);
                AssertVideoTimeline(partial, "recovered");

                recorder.Stop();
                Assert.IsTrue(recorder.CompletedSuccessfully);
            }
            finally
            {
                recorder.Dispose();
            }

            Assert.AreEqual(frames, ProbeFile(path).VideoPacketCount);
        }

        [TestMethod, Timeout(120_000)]
        public void Mp4_LongStaticVfrRecording_IsRecoverableUpToTheLastKeepAlivePair()
        {
            // Automatic frame rate with a static screen: one picture, then a keep-alive repeat per
            // second. Every second repeat is a forced keyframe and closes a fragment, so a
            // recording with almost no frames should still be recoverable to within ~2 s.
            // Expected: the frames at 0..7 s are readable while the recorder is at 9 s.
            // Actual: the file on disk is the 88-byte stub (a static screen takes a very long
            // time to produce 256 KiB).
            string path = PathFor("static.mp4");
            string snapshot = PathFor("static-snapshot.mp4");

            var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps, variableFrameRate: true), null);
            try
            {
                recorder.Start();
                RecorderFeed.SubmitPattern(recorder, Width, Height, 0, 0);
                for (int second = 1; second <= 9; second++)
                    RecorderFeed.Repeat(recorder, second * Fps);
                RecorderFeed.WaitForVideoEncoded(recorder);

                MediaProbe partial = null;
                RecorderFeed.WaitUntil(() =>
                {
                    TestFiles.CopyShared(path, snapshot);
                    if (new FileInfo(snapshot).Length < 1024)
                        return false;
                    partial = MediaProbe.Probe(snapshot);
                    return partial.Opened && partial.VideoPacketCount >= 8;
                }, "the fragments to be written to disk", 5_000);

                // Frames at 0..9 s; the fragment starting at the 8 s keyframe is still open.
                Assert.AreEqual(8, partial.VideoPacketCount);
                for (int i = 0; i < 8; i++)
                    Assert.AreEqual(i, partial.VideoPts[i], 1e-6, $"PTS of recovered packet {i}");

                recorder.Stop();
                Assert.IsTrue(recorder.CompletedSuccessfully);
            }
            finally
            {
                recorder.Dispose();
            }

            Assert.AreEqual(10, ProbeFile(path).VideoPacketCount);
        }

        [TestMethod, Timeout(120_000)]
        public void Mp4_WhileStillRecording_OnceEnoughDataWasWritten_TheFileIsPlayable()
        {
            // The same scenario with enough data to get past libavformat's 256 KiB write buffer:
            // incompressible content at 6 Mbps for 5 s → fragments of ~1.5 MB at 2 s and 4 s.
            // The fragment mechanism itself works: what has reached the disk is a valid
            // fragmented MP4 containing (at least) the complete first fragment.
            const int frames = 150;
            const int audioBlocks = frames * SampleRate / Fps / Block;
            string path = PathFor("live-big.mp4");
            string snapshot = PathFor("snapshot-after-crash-big.mp4");

            var recorder = new Recorder(path, "mp4", SoftwareVideo(Fps, bitrate: 6_000_000), StereoAudio());
            try
            {
                recorder.Start();
                MediaProbe partial = SnapshotWhileRecording(recorder, path, snapshot, frames, noise: true, minimumVideoPackets: 2 * Fps, timeoutMs: 20_000);

                Assert.IsTrue(recorder.BytesWritten > 1_000_000, "BytesWritten must advance while recording");
                Assert.AreEqual(2, partial.StreamCount);
                Assert.AreEqual("h264", partial.VideoCodec);
                Assert.AreEqual(Width, partial.Width);
                Assert.AreEqual(Height, partial.Height);
                Assert.AreEqual("aac", partial.AudioCodec);

                // At least the first fragment (2 s), contiguous from the start, beginning with a
                // keyframe, with its audio — and the pictures really decode.
                Assert.IsTrue(partial.VideoPacketCount >= 2 * Fps && partial.VideoPacketCount <= frames, $"{partial.VideoPacketCount} video packets are recoverable");
                Assert.IsTrue(partial.VideoKeyFrames[0]);
                Assert.IsTrue(partial.VideoKeyFrames[2 * Fps], "the second fragment starts with a keyframe");
                AssertVideoTimeline(partial, "recovered");
                Assert.IsTrue(partial.AudioPacketCount >= 2 * SampleRate / Block - 2, $"only {partial.AudioPacketCount} audio packets are recoverable");
                Assert.IsTrue(MediaProbe.DecodeVideo(snapshot, 2 * Fps).Count == 2 * Fps, "the recovered first fragment must decode");
                TestContext.WriteLine($"Recoverable mid-recording: {partial.VideoPacketCount} of {frames} video packets, {partial.AudioPacketCount} audio packets " +
                                      $"({new FileInfo(snapshot).Length} bytes on disk, {recorder.BytesWritten} bytes written by the muxer).");

                // A normal stop then produces the complete, regular file.
                recorder.Stop();
                Assert.IsTrue(recorder.CompletedSuccessfully);
            }
            finally
            {
                recorder.Dispose();
            }

            MediaProbe final = ProbeFile(path);
            Assert.AreEqual(frames, final.VideoPacketCount);
            Assert.IsTrue(final.AudioPacketCount >= audioBlocks);
            AssertVideoTimeline(final, "final");
            Assert.AreEqual(5.0, final.DurationSeconds, 0.06, "duration of the finished file");
            Assert.AreEqual(frames, MediaProbe.DecodeVideo(path).Count);
        }

        #endregion
    }
}
