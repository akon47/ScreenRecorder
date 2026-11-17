using System;
using System.IO;
using System.Linq;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Encoder;
using ScreenRecorder.Tests.Support;

namespace ScreenRecorder.Tests.Encoding
{
    /// <summary>The recorder's audio path: PCM blocks → AAC / MP3 → container.</summary>
    [TestClass]
    [TestCategory("Integration")]
    public sealed unsafe class RecorderAudioTests : RecorderTestBase
    {
        private const int Block = 1024;
        private const int SampleRate = 48000;

        /// <summary>RMS of the test tone (amplitude 0.3 sine).</summary>
        private static readonly double ToneRms = SinePcm.Amplitude / Math.Sqrt(2);

        private static double Seconds(int blocks) => blocks * (double)Block / SampleRate;

        [TestMethod, Timeout(120_000)]
        public void VideoAndAudio_AudioHasTheExpectedDuration_AndStartsWithTheVideo()
        {
            const int fps = 30;
            const int frames = 90;                               // 3.0 s
            const int blocks = 3 * SampleRate / Block;           // 140 blocks = 2.987 s
            string path = PathFor("av.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(fps), StereoAudio());

            Assert.IsTrue(recorder.HasVideo);
            Assert.IsTrue(recorder.HasAudio);
            recorder.Start();

            // Interleave like a live recording: audio keeps pace with the video grid.
            var sine = new SinePcm(SampleRate, 2);
            int pushedBlocks = 0;
            for (int i = 0; i < frames; i++)
            {
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
                int blocksDue = Math.Min(blocks, (int)((i + 1) * (long)SampleRate / fps / Block));
                RecorderFeed.PushSineS16(recorder, sine, blocksDue - pushedBlocks);
                pushedBlocks = blocksDue;
            }

            RecorderFeed.PushSineS16(recorder, sine, blocks - pushedBlocks);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.IsNull(recorder.Fault);
            Assert.AreEqual(0, recorder.DroppedFrames);

            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(2, probe.StreamCount);
            Assert.AreEqual("h264", probe.VideoCodec);
            Assert.AreEqual("aac", probe.AudioCodec);
            Assert.AreEqual(SampleRate, probe.SampleRate);
            Assert.AreEqual(2, probe.Channels);
            Assert.AreEqual(frames, probe.VideoPacketCount);

            // One AAC packet per 1024-sample block (+ the encoder's priming/flush packets).
            Assert.IsTrue(probe.AudioPacketCount >= blocks && probe.AudioPacketCount <= blocks + 2,
                $"{probe.AudioPacketCount} audio packets for {blocks} blocks");

            // Audio packets are contiguous 1024-sample steps: no gap, no overlap.
            for (int i = 1; i < probe.AudioPts.Count; i++)
                Assert.AreEqual(Seconds(1), probe.AudioPts[i] - probe.AudioPts[i - 1], 1e-4, $"audio packet {i} spacing");

            double audioDuration = probe.AudioPts[probe.AudioPts.Count - 1] + Seconds(1) - Math.Max(0, probe.AudioPts[0]);
            Assert.AreEqual(Seconds(blocks), audioDuration, 0.05, "audio stream duration");

            // Both streams start together (AAC priming puts the first packet up to 21 ms early).
            Assert.AreEqual(0.0, probe.VideoPts[0], 1e-6, "first video PTS");
            Assert.IsTrue(Math.Abs(probe.VideoPts[0] - probe.AudioPts[0]) < 0.05,
                $"video starts at {probe.VideoPts[0]:0.###} s, audio at {probe.AudioPts[0]:0.###} s");

            Assert.AreEqual(3.0, probe.DurationSeconds, 0.06, "container duration");
        }

        [TestMethod, Timeout(120_000)]
        public void VideoAndAudio_TheToneSurvivesEncoding()
        {
            const int blocks = 94; // ~2 s
            string path = PathFor("tone.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(30), StereoAudio());
            recorder.Start();

            for (int i = 0; i < 60; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
            RecorderFeed.PushSineS16(recorder, new SinePcm(SampleRate, 2), blocks);
            recorder.Stop();

            (double rms, double peak) = MediaProbe.MeasureAudioLevel(path);

            Assert.AreEqual(ToneRms, rms, 0.03, "decoded RMS");
            Assert.IsTrue(peak > 0.25 && peak < 0.40, $"decoded peak {peak:0.###} for a 0.3 amplitude sine");
        }

        [TestMethod, Timeout(120_000)]
        public void VideoAndAudio_SilentInput_StaysSilent()
        {
            // The audio source emits silence blocks when nothing plays: no DC offset, no noise.
            const int blocks = 47;
            string path = PathFor("silence.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(30), StereoAudio());
            recorder.Start();

            for (int i = 0; i < 30; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);

            var silence = new short[Block * 2];
            fixed (short* p = silence)
            {
                for (int i = 0; i < blocks; i++)
                    recorder.PushAudioFrame(new RawAudioFrame((nint)p, Block, SampleRate, 2, SampleFormat.S16, 0));
            }

            recorder.Stop();

            MediaProbe probe = ProbeFile(path);
            Assert.IsTrue(probe.AudioPacketCount >= blocks);
            (double rms, double peak) = MediaProbe.MeasureAudioLevel(path);
            Assert.IsTrue(peak < 0.001, $"silence decoded with peak {peak}");
            Assert.IsTrue(rms < 0.0005, $"silence decoded with rms {rms}");
        }

        [TestMethod, Timeout(120_000)]
        public void FloatInput_IsEncodedAtTheSameLevelAsS16()
        {
            const int blocks = 94;
            string path = PathFor("float.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(30), StereoAudio(format: SampleFormat.FLT));
            recorder.Start();

            for (int i = 0; i < 60; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);

            var sine = new SinePcm(SampleRate, 2);
            var pcm = new float[Block * 2];
            fixed (float* p = pcm)
            {
                for (int i = 0; i < blocks; i++)
                {
                    sine.Fill(pcm, Block);
                    recorder.PushAudioFrame(new RawAudioFrame((nint)p, Block, SampleRate, 2, SampleFormat.FLT, 0));
                }
            }

            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            MediaProbe probe = ProbeFile(path);
            Assert.IsTrue(probe.AudioPacketCount >= blocks && probe.AudioPacketCount <= blocks + 2);
            Assert.AreEqual(ToneRms, MediaProbe.MeasureAudioLevel(path).Rms, 0.03, "decoded RMS");
        }

        [TestMethod, Timeout(120_000)]
        public void OddBlockSizes_AreRepackedWithoutLosingSamples()
        {
            // WASAPI delivers 480-sample periods; the AAC encoder needs 1024-sample frames.
            const int inputBlock = 480;
            const int inputBlocks = 200; // 96 000 samples = 2.0 s
            string path = PathFor("odd-blocks.m4a");
            using var recorder = new Recorder(path, "ipod", null, StereoAudio());
            recorder.Start();

            RecorderFeed.PushSineS16(recorder, new SinePcm(SampleRate, 2), inputBlocks, inputBlock);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            MediaProbe probe = ProbeFile(path);

            // 96 000 samples = 93.75 AAC frames → 94 (last one padded) + priming/flush.
            Assert.IsTrue(probe.AudioPacketCount >= 94 && probe.AudioPacketCount <= 96, $"{probe.AudioPacketCount} packets");
            Assert.AreEqual(2.0, probe.DurationSeconds, 0.06);
            Assert.AreEqual(ToneRms, MediaProbe.MeasureAudioLevel(path).Rms, 0.03, "no dropped or repeated samples (level intact)");
        }

        [TestMethod, Timeout(120_000)]
        [DataRow("mp4", "mp4", AudioCodec.Aac)]
        [DataRow("mov", "mov", AudioCodec.Aac)]
        [DataRow("matroska", "mkv", AudioCodec.Aac)]
        [DataRow("mpegts", "ts", AudioCodec.Aac)]
        [DataRow("mp4", "mp4", AudioCodec.Mp3)]
        [DataRow("matroska", "mkv", AudioCodec.Mp3)]
        [DataRow("avi", "avi", AudioCodec.Mp3)]
        public void VideoAndAudio_AnEventAtTheSameInstant_IsInSyncInTheFile(string format, string extension, AudioCodec audioCodec)
        {
            AssertEventIsInSync(format, extension, audioCodec);
        }

        [TestMethod, Timeout(120_000)]
        public void VideoAndAudio_AnEventAtTheSameInstant_IsInSyncInTheFile_AviWithAac()
        {
            AssertEventIsInSync("avi", "avi", AudioCodec.Aac);
        }

        private void AssertEventIsInSync(string format, string extension, AudioCodec audioCodec)
        {
            // The picture turns from dark to bright at exactly 1.0 s (frame 30 of 30 fps) and a
            // tone starts at exactly 1.0 s (sample 48 000). After encoding, muxing, demuxing and
            // decoding, both events must still coincide — whatever each container does with the
            // audio encoder's priming samples and its own start offset.
            const int fps = 30;
            const int frames = 60;
            const int eventFrame = 30;
            const int eventSample = eventFrame * SampleRate / fps;
            const int totalSamples = 2 * SampleRate;
            string path = PathFor("sync." + extension);

            var video = SoftwareVideo(fps);
            video.RateControl = RateControl.Cq;
            video.Quality = 18;
            using (var recorder = new Recorder(path, format, video, StereoAudio(audioCodec)))
            {
                recorder.Start();

                var pcm = new short[Block * 2];
                long sample = 0;
                fixed (short* p = pcm)
                {
                    for (int i = 0; i < frames; i++)
                    {
                        byte luma = i < eventFrame ? (byte)40 : (byte)210;
                        RecorderFeed.SubmitSplit(recorder, Width, Height, Width, i, luma, luma);

                        // Audio up to the end of this video frame, in 1024-sample blocks.
                        long due = Math.Min(totalSamples, (i + 1) * (long)SampleRate / fps);
                        while (sample + Block <= due)
                        {
                            for (int s = 0; s < Block; s++)
                            {
                                long n = sample + s;
                                short value = n < eventSample ? (short)0 : (short)(Math.Sin((n - eventSample) * 2 * Math.PI * 1000 / SampleRate) * 0.5 * short.MaxValue);
                                pcm[s * 2] = value;
                                pcm[s * 2 + 1] = value;
                            }

                            recorder.PushAudioFrame(new RawAudioFrame((nint)p, Block, SampleRate, 2, SampleFormat.S16, 0));
                            sample += Block;
                        }
                    }
                }

                recorder.Stop();
                Assert.IsTrue(recorder.CompletedSuccessfully);
            }

            var decoded = MediaProbe.DecodeVideo(path);
            Assert.AreEqual(frames, decoded.Count);
            DecodedVideoFrame firstBright = decoded.First(f => f.MeanLuma() > 125);
            double videoStart = decoded[0].PtsSeconds;
            double videoEvent = firstBright.PtsSeconds - videoStart;
            double audioEvent = MediaProbe.FindAudioOnset(path, threshold: 0.1) - videoStart;

            Assert.IsFalse(double.IsNaN(audioEvent), "the tone was not found in the decoded audio");
            TestContext.WriteLine($"{format}/{audioCodec}: picture changes at {videoEvent * 1000:0.0} ms, tone starts at {audioEvent * 1000:0.0} ms (difference {(audioEvent - videoEvent) * 1000:0.0} ms)");

            // Both at ~1.0 s after the first picture (up to one frame later where the container
            // shifted the streams for the audio priming) ...
            Assert.IsTrue(videoEvent >= 1.0 - 1e-3 && videoEvent <= 1.0 + 1.0 / fps + 1e-3, $"picture changes at {videoEvent:0.####} s");

            // ... and within one video frame of each other (MP3 decoders add up to ~1100 samples
            // of their own delay where the container does not carry the encoder delay).
            Assert.AreEqual(videoEvent, audioEvent, audioCodec == AudioCodec.Mp3 ? 2.0 / fps : 1.0 / fps, "audio/video offset");
        }

        #region Audio only

        [TestMethod, Timeout(120_000)]
        public void AudioOnly_M4a_HasOneAacStreamAndNoVideo()
        {
            const int blocks = 141; // 3.008 s
            string path = PathFor("audio.m4a");
            using var recorder = new Recorder(path, "ipod", null, StereoAudio(AudioCodec.Aac));

            Assert.IsFalse(recorder.HasVideo);
            Assert.IsTrue(recorder.HasAudio);
            recorder.Start();
            Assert.IsNull(recorder.VideoEncoderName);

            RecorderFeed.PushSineS16(recorder, new SinePcm(SampleRate, 2), blocks);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.IsNull(recorder.Fault);
            Assert.AreEqual(0, recorder.RecordedVideoFrames);
            Assert.IsTrue(recorder.BytesWritten > 0);

            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(1, probe.StreamCount);
            Assert.IsFalse(probe.HasVideo);
            Assert.AreEqual(0, probe.VideoPacketCount);
            Assert.AreEqual("aac", probe.AudioCodec);
            Assert.AreEqual(SampleRate, probe.SampleRate);
            Assert.AreEqual(2, probe.Channels);
            Assert.IsTrue(probe.AudioPacketCount >= blocks && probe.AudioPacketCount <= blocks + 2, $"{probe.AudioPacketCount} packets for {blocks} blocks");
            Assert.AreEqual(Seconds(blocks), probe.DurationSeconds, 0.06, "duration");
            Assert.AreEqual(ToneRms, MediaProbe.MeasureAudioLevel(path).Rms, 0.03, "decoded RMS");
        }

        [TestMethod, Timeout(120_000)]
        public void AudioOnly_Mp3_HasOneMp3Stream()
        {
            const int blocks = 141; // 144 384 samples = 3.008 s
            string path = PathFor("audio.mp3");
            using var recorder = new Recorder(path, "mp3", null, StereoAudio(AudioCodec.Mp3));
            recorder.Start();

            RecorderFeed.PushSineS16(recorder, new SinePcm(SampleRate, 2), blocks);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.IsNull(recorder.Fault);

            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(1, probe.StreamCount);
            Assert.IsFalse(probe.HasVideo);
            Assert.AreEqual("mp3", probe.AudioCodec);
            Assert.AreEqual(SampleRate, probe.SampleRate);
            Assert.AreEqual(2, probe.Channels);

            // MP3 frames hold 1152 samples: 144 384 / 1152 = 125.3 frames (+ encoder delay).
            int expectedFrames = blocks * Block / 1152;
            Assert.IsTrue(probe.AudioPacketCount >= expectedFrames && probe.AudioPacketCount <= expectedFrames + 3,
                $"{probe.AudioPacketCount} MP3 frames, expected about {expectedFrames}");
            Assert.AreEqual(Seconds(blocks), probe.DurationSeconds, 0.1, "duration");
            Assert.AreEqual(ToneRms, MediaProbe.MeasureAudioLevel(path).Rms, 0.03, "decoded RMS");
        }

        [TestMethod, Timeout(120_000)]
        public void AudioOnly_VideoInputIsRejected()
        {
            string path = PathFor("audio-novideo.m4a");
            using var recorder = new Recorder(path, "ipod", null, StereoAudio());
            recorder.Start();

            Assert.AreEqual((nint)0, recorder.RentVideoBuffer(Width * Height * 3 / 2));
            Assert.IsFalse(recorder.RepeatLastVideoFrame(0));

            RecorderFeed.PushSineS16(recorder, new SinePcm(SampleRate, 2), 20);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            Assert.AreEqual(0, recorder.RecordedVideoFrames);
            Assert.AreEqual(1, ProbeFile(path).StreamCount);
        }

        [TestMethod, Timeout(120_000)]
        public void VideoOnly_AudioInputIsIgnored()
        {
            string path = PathFor("video-noaudio.mp4");
            using var recorder = new Recorder(path, "mp4", SoftwareVideo(30), null);
            recorder.Start();

            RecorderFeed.PushSineS16(recorder, new SinePcm(SampleRate, 2), 20);
            for (int i = 0; i < 10; i++)
                RecorderFeed.SubmitPattern(recorder, Width, Height, i, i);
            recorder.Stop();

            Assert.IsTrue(recorder.CompletedSuccessfully);
            MediaProbe probe = ProbeFile(path);
            Assert.AreEqual(1, probe.StreamCount);
            Assert.IsFalse(probe.HasAudio);
            Assert.AreEqual(10, probe.VideoPacketCount);
        }

        #endregion
    }
}
