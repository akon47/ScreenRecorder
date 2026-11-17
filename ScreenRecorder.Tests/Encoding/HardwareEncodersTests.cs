using System;
using System.Linq;
using FFmpeg.AutoGen;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Encoder;
using ScreenRecorder.Tests.Support;

namespace ScreenRecorder.Tests.Encoding
{
    /// <summary>
    /// Hardware-encoder detection and what the recorder does with it. The detection really
    /// opens the encoders, so the answers differ from machine to machine; the tests assert the
    /// rules that must hold on every machine (and report what was found).
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    [TestCategory("Hardware")]
    public sealed unsafe class HardwareEncodersTests : RecorderTestBase
    {
        private static readonly VideoCodec[] Codecs = { VideoCodec.H264, VideoCodec.Hevc, VideoCodec.Av1 };
        private static readonly HwAccel[] Backends = { HwAccel.Nvenc, HwAccel.Qsv, HwAccel.Amf };

        [TestMethod, Timeout(120_000)]
        public void Detection_IsConsistentWithItself()
        {
            HardwareEncoders.Detect();

            foreach (VideoCodec codec in Codecs)
            {
                bool[] available = Backends.Select(b => HardwareEncoders.IsAvailable(codec, b)).ToArray();
                TestContext.WriteLine($"{codec}: " + string.Join(", ", Backends.Select((b, i) => $"{b}={available[i]}")));

                // The preferred backend is the first working one in the order NVENC, Quick Sync, AMF.
                int first = Array.IndexOf(available, true);
                HwAccel expectedBest = first < 0 ? HwAccel.Software : Backends[first];
                Assert.AreEqual(expectedBest, HardwareEncoders.GetBest(codec), $"{codec}: preferred backend");
                Assert.AreEqual(expectedBest, HardwareEncoders.GetBest(codec, RateControl.Cbr));
                Assert.AreEqual(expectedBest, HardwareEncoders.GetBest(codec, RateControl.Vbr));
                Assert.AreEqual(first >= 0, HardwareEncoders.IsAvailable(codec), $"{codec}: any hardware encoder");

                // Constant quality never picks Quick Sync (its ICQ mode is unverified).
                HwAccel expectedCq = available[0] ? HwAccel.Nvenc : available[2] ? HwAccel.Amf : HwAccel.Software;
                Assert.AreEqual(expectedCq, HardwareEncoders.GetBest(codec, RateControl.Cq), $"{codec}: preferred backend for constant quality");
            }
        }

        [TestMethod, Timeout(120_000)]
        public void IsAvailable_SoftwareIsNotAHardwareBackend_AndUnknownCodecsHaveNone()
        {
            foreach (VideoCodec codec in Codecs)
                Assert.IsFalse(HardwareEncoders.IsAvailable(codec, HwAccel.Software), $"{codec}: Software reported as a hardware backend");

            foreach (VideoCodec codec in new[] { VideoCodec.None, VideoCodec.Default, (VideoCodec)99 })
            {
                foreach (HwAccel backend in Backends)
                    Assert.IsFalse(HardwareEncoders.IsAvailable(codec, backend), $"{codec} on {backend}");

                Assert.IsFalse(HardwareEncoders.IsAvailable(codec));
                Assert.AreEqual(HwAccel.Software, HardwareEncoders.GetBest(codec));
            }

            Assert.IsFalse(HardwareEncoders.IsAvailable(VideoCodec.H264, (HwAccel)99));
        }

        [TestMethod, Timeout(120_000)]
        public void H265_IsTheSameAsHevc()
        {
            foreach (HwAccel backend in Backends)
                Assert.AreEqual(HardwareEncoders.IsAvailable(VideoCodec.Hevc, backend), HardwareEncoders.IsAvailable(VideoCodec.H265, backend));

            Assert.AreEqual(HardwareEncoders.GetBest(VideoCodec.Hevc), HardwareEncoders.GetBest(VideoCodec.H265));
        }

        [TestMethod, Timeout(120_000)]
        public void Detect_Again_GivesTheSameAnswers()
        {
            bool[] before = Codecs.SelectMany(c => Backends.Select(b => HardwareEncoders.IsAvailable(c, b))).ToArray();

            HardwareEncoders.Detect();

            bool[] after = Codecs.SelectMany(c => Backends.Select(b => HardwareEncoders.IsAvailable(c, b))).ToArray();
            CollectionAssert.AreEqual(before, after);
        }

        [TestMethod, Timeout(120_000)]
        public void Recorder_BackendThatDoesNotWorkHere_FallsBackToSoftware()
        {
            // "A machine-specific hardware failure costs performance, never the recording."
            HwAccel[] broken = Backends.Where(b => !HardwareEncoders.IsAvailable(VideoCodec.H264, b)).ToArray();
            if (broken.Length == 0)
                Assert.Inconclusive("Every hardware backend works on this machine: nothing to fall back from.");

            foreach (HwAccel backend in broken)
            {
                string path = PathFor($"fallback-{backend}.mp4");
                var video = SoftwareVideo(30);
                video.Hw = backend;
                using var recorder = new Recorder(path, "mp4", video, null);
                recorder.Start();

                Assert.AreEqual(HwAccel.Software, recorder.ActualHwAccel, $"requested {backend}");
                Assert.AreEqual("libx264", recorder.VideoEncoderName);
                Assert.IsFalse(recorder.UsesGpuInput);

                for (int i = 0; i < 15; i++)
                    RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
                recorder.Stop();

                Assert.IsTrue(recorder.CompletedSuccessfully);
                Assert.AreEqual(15, ProbeFile(path).VideoPacketCount, $"requested {backend}");
            }
        }

        [TestMethod, Timeout(120_000)]
        public void Recorder_EveryHardwareEncoderFoundHere_RecordsExactFramesFromCpuInput()
        {
            // The path used when the zero-copy GPU hand-over is not possible: CPU NV12 frames
            // into the hardware encoder. 1280x720 because hardware encoders have minimum sizes.
            const int width = 1280, height = 720, fps = 30, frames = 45;
            var found = (from codec in Codecs
                         from backend in Backends
                         where HardwareEncoders.IsAvailable(codec, backend)
                         select (Codec: codec, Backend: backend)).ToList();
            if (found.Count == 0)
                Assert.Inconclusive("No hardware video encoder works on this machine.");

            foreach (var (codec, backend) in found)
            {
                string path = PathFor($"hw-{codec}-{backend}.mp4");
                var video = SoftwareVideo(fps, codec: codec, width: width, height: height, bitrate: 4_000_000);
                video.Hw = backend;
                using var recorder = new Recorder(path, "mp4", video, null);
                recorder.Start();

                Assert.AreEqual(backend, recorder.ActualHwAccel, $"{codec}: detection said {backend} works, the recorder could not open it");
                Assert.AreEqual(FFmpegVideoEncoder.ResolveEncoderNames(codec, backend)[0], recorder.VideoEncoderName);
                Assert.IsFalse(recorder.UsesGpuInput);

                for (int i = 0; i < frames; i++)
                    RecorderFeed.SubmitPattern(recorder, width, height, i, i);
                recorder.Stop();

                Assert.IsNull(recorder.Fault, $"{codec}/{backend}: {recorder.Fault}");
                Assert.IsTrue(recorder.CompletedSuccessfully, $"{codec}/{backend}");
                Assert.AreEqual(0, recorder.DroppedFrames, $"{codec}/{backend}");

                MediaProbe probe = ProbeFile(path);
                string expectedCodec = codec == VideoCodec.H264 ? "h264" : codec == VideoCodec.Hevc ? "hevc" : "av1";
                Assert.AreEqual(expectedCodec, probe.VideoCodec);
                Assert.AreEqual(width, probe.Width);
                Assert.AreEqual(height, probe.Height);
                Assert.AreEqual(frames, probe.VideoPacketCount, $"{codec}/{backend}: video packets");
                for (int i = 0; i < frames; i++)
                    Assert.AreEqual(i / (double)fps, probe.VideoPts[i], 1e-6, $"{codec}/{backend}: PTS of packet {i}");
                Assert.IsTrue(probe.VideoKeyFrames[0], $"{codec}/{backend}: first packet is not a keyframe");
                Assert.AreEqual(frames / (double)fps, probe.DurationSeconds, 0.02, $"{codec}/{backend}: duration");
                TestContext.WriteLine($"{codec} on {backend} ({recorder.VideoEncoderName ?? FFmpegVideoEncoder.ResolveEncoderNames(codec, backend)[0]}): {probe.VideoPacketCount} packets, {new System.IO.FileInfo(path).Length} bytes");
            }
        }

        /// <summary>
        /// Records 3 s of grainy 1280x720 content — far more than 4 Mbps worth when left to the
        /// encoder, but compressible down to it — at a 4 Mbps constant bitrate on the preferred
        /// hardware encoder for <paramref name="codec"/> and checks the resulting rate.
        /// (Full-range noise is no use here: at its coarsest quantizer AV1 on NVENC still needs
        /// about 7 Mbps for it, with the ffmpeg command line just the same.)
        /// </summary>
        private void AssertHardwareCbrHonorsTheBitrate(VideoCodec codec)
        {
            const int width = 1280, height = 720, fps = 30, frames = 90, bitrate = 4_000_000;
            HwAccel backend = HardwareEncoders.GetBest(codec);
            if (backend == HwAccel.Software)
                Assert.Inconclusive($"No hardware {codec} encoder works on this machine.");

            string path = PathFor($"cbr-{codec}.mp4");
            var video = SoftwareVideo(fps, codec: codec, width: width, height: height, bitrate: bitrate);
            video.Hw = backend;
            video.RateControl = RateControl.Cbr;
            using (var recorder = new Recorder(path, "mp4", video, null))
            {
                recorder.Start();
                Assert.AreEqual(backend, recorder.ActualHwAccel);
                for (int i = 0; i < frames; i++)
                    RecorderFeed.SubmitGrain(recorder, width, height, i, seed: i, amplitude: 12);
                recorder.Stop();
                Assert.IsTrue(recorder.CompletedSuccessfully);
            }

            Assert.AreEqual(frames, ProbeFile(path).VideoPacketCount);
            double megabitsPerSecond = new System.IO.FileInfo(path).Length * 8 / (frames / (double)fps) / 1e6;
            TestContext.WriteLine($"{codec} on {backend}: {megabitsPerSecond:0.00} Mbps for a 4.00 Mbps constant-bitrate target");
            Assert.AreEqual(bitrate / 1e6, megabitsPerSecond, bitrate / 1e6 * 0.4, $"{codec} on {backend}: constant bitrate");
        }

        [TestMethod, Timeout(120_000)]
        [DataRow(VideoCodec.H264)]
        [DataRow(VideoCodec.Hevc)]
        public void Recorder_HardwareConstantBitrate_HonorsTheBitrate(VideoCodec codec)
        {
            AssertHardwareCbrHonorsTheBitrate(codec);
        }

        [TestMethod, Timeout(120_000)]
        public void Recorder_HardwareConstantBitrate_HonorsTheBitrate_Av1()
        {
            AssertHardwareCbrHonorsTheBitrate(VideoCodec.Av1);
        }

        [TestMethod, Timeout(120_000)]
        [DataRow("mp4", "mp4")]
        [DataRow("matroska", "mkv")]
        public void Recorder_Av1Software_WritesAPlayableFile(string format, string extension)
        {
            if (ffmpeg.avcodec_find_encoder_by_name("libsvtav1") == null)
                Assert.Inconclusive("This FFmpeg build has no SVT-AV1 encoder.");

            const int fps = 30, frames = 45;
            Assert.IsTrue(MediaFormat.SupportsVideoCodec(format, VideoCodec.Av1));
            string path = PathFor("av1." + extension);

            using (var recorder = new Recorder(path, format, SoftwareVideo(fps, codec: VideoCodec.Av1), null))
            {
                recorder.Start();
                Assert.AreEqual("libsvtav1", recorder.VideoEncoderName);
                Assert.AreEqual(HwAccel.Software, recorder.ActualHwAccel);

                for (int i = 0; i < frames; i++)
                    RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
                recorder.Stop();

                Assert.IsNull(recorder.Fault, recorder.Fault?.ToString());
                Assert.IsTrue(recorder.CompletedSuccessfully);
                Assert.AreEqual(0, recorder.DroppedFrames);
            }

            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual("av1", probe.VideoCodec);
            Assert.AreEqual(Width, probe.Width);
            Assert.AreEqual(Height, probe.Height);
            Assert.AreEqual(frames, probe.VideoPacketCount, "one packet per frame");
            for (int i = 0; i < frames; i++)
                Assert.AreEqual(i / (double)fps, probe.VideoPts[i], 0.001, $"PTS of packet {i}");
            Assert.IsTrue(probe.VideoKeyFrames[0]);
            Assert.AreEqual(frames / (double)fps, probe.DurationSeconds, 0.05, "duration");
        }
    }
}
