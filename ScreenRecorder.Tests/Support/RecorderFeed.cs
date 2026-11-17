using System;
using System.Diagnostics;
using System.Threading;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.Tests.Support
{
    /// <summary>
    /// Feeds a <see cref="Recorder"/> deterministically. The recorder's capture-facing API never
    /// blocks — it drops (and counts) a frame when the encoder is behind — so the helpers wait
    /// for the encoder to catch up BEFORE offering the next frame. Nothing is ever dropped and
    /// tests can assert exact frame counts and <c>DroppedFrames == 0</c>.
    /// </summary>
    internal static unsafe class RecorderFeed
    {
        public const int DefaultTimeoutMs = 30_000;

        /// <summary>Polls (yielding, never sleeping a whole timer tick) until the condition holds.</summary>
        public static void WaitUntil(Func<bool> condition, string description, int timeoutMs = DefaultTimeoutMs)
        {
            var stopwatch = Stopwatch.StartNew();
            var spin = new SpinWait();
            while (!condition())
            {
                if (stopwatch.ElapsedMilliseconds > timeoutMs)
                    Assert.Fail($"Timed out after {timeoutMs} ms waiting for {description}.");
                spin.SpinOnce(sleep1Threshold: -1);
            }
        }

        /// <summary>
        /// Waits until at most one accepted frame is still waiting for the encoder, which
        /// guarantees the next rent/submit/repeat is accepted.
        /// </summary>
        /// <param name="neverEncoded">Accepted frames known to be discarded by the encoder thread.</param>
        public static void WaitForVideoCapacity(Recorder recorder, long neverEncoded = 0)
        {
            WaitUntil(
                () => recorder.Fault != null
                      || recorder.RecordedVideoFrames - recorder.EncodedVideoFrames - neverEncoded < 2,
                "the video encoder to catch up");

            if (recorder.Fault != null)
                Assert.Fail("The recorder faulted: " + recorder.Fault);
        }

        /// <summary>Waits until every accepted video frame went through the encoder.</summary>
        public static void WaitForVideoEncoded(Recorder recorder, long neverEncoded = 0)
        {
            WaitUntil(
                () => recorder.Fault != null
                      || recorder.RecordedVideoFrames - recorder.EncodedVideoFrames - neverEncoded <= 0,
                "all video frames to be encoded");

            if (recorder.Fault != null)
                Assert.Fail("The recorder faulted: " + recorder.Fault);
        }

        /// <summary>Rents a buffer (after waiting for capacity) and fails the test if none is handed out.</summary>
        public static nint Rent(Recorder recorder, int bytes)
        {
            WaitForVideoCapacity(recorder);
            nint buffer = recorder.RentVideoBuffer(bytes);
            Assert.AreNotEqual((nint)0, buffer, "RentVideoBuffer returned 0 although the encoder was idle.");
            return buffer;
        }

        /// <summary>Submits one animated test-pattern frame (stride == width) at a grid index.</summary>
        public static void SubmitPattern(Recorder recorder, int width, int height, long gridIndex, int patternIndex)
        {
            int bytes = width * height * 3 / 2;
            nint buffer = Rent(recorder, bytes);
            Nv12TestPattern.Fill(buffer, width, width, height, patternIndex);
            Assert.IsTrue(recorder.SubmitVideoBuffer(buffer, width, bytes, gridIndex), $"Frame at grid index {gridIndex} was not accepted.");
        }

        /// <summary>Submits one frame of incompressible noise (stride == width) at a grid index.</summary>
        public static void SubmitNoise(Recorder recorder, int width, int height, long gridIndex, int seed)
        {
            int bytes = width * height * 3 / 2;
            nint buffer = Rent(recorder, bytes);
            Nv12TestPattern.FillNoise(buffer, width, width, height, seed);
            Assert.IsTrue(recorder.SubmitVideoBuffer(buffer, width, bytes, gridIndex), $"Frame at grid index {gridIndex} was not accepted.");
        }

        /// <summary>Submits one frame of light grain (see <see cref="Nv12TestPattern.FillGrain"/>).</summary>
        public static void SubmitGrain(Recorder recorder, int width, int height, long gridIndex, int seed, int amplitude)
        {
            int bytes = width * height * 3 / 2;
            nint buffer = Rent(recorder, bytes);
            Nv12TestPattern.FillGrain(buffer, width, width, height, seed, amplitude);
            Assert.IsTrue(recorder.SubmitVideoBuffer(buffer, width, bytes, gridIndex), $"Frame at grid index {gridIndex} was not accepted.");
        }

        /// <summary>Submits one flat two-halves frame with an arbitrary (possibly padded) stride.</summary>
        public static void SubmitSplit(Recorder recorder, int width, int height, int stride, long gridIndex,
            byte leftLuma, byte rightLuma, byte u = 128, byte v = 128)
        {
            int bytes = stride * height * 3 / 2;
            nint buffer = Rent(recorder, bytes);
            Nv12TestPattern.FillSplit(buffer, stride, width, height, leftLuma, rightLuma, u, v);
            Assert.IsTrue(recorder.SubmitVideoBuffer(buffer, stride, bytes, gridIndex), $"Frame at grid index {gridIndex} was not accepted.");
        }

        /// <summary>Queues a repeat of the last picture and asserts it was accepted.</summary>
        public static void Repeat(Recorder recorder, long gridIndex)
        {
            WaitForVideoCapacity(recorder);
            Assert.IsTrue(recorder.RepeatLastVideoFrame(gridIndex), $"Repeat at grid index {gridIndex} was not accepted.");
        }

        /// <summary>
        /// Pushes <paramref name="blocks"/> blocks of interleaved S16 sine. The recorder's audio
        /// input queue holds 256 blocks and drops silently beyond that, so callers keep a burst
        /// well below that.
        /// </summary>
        public static void PushSineS16(Recorder recorder, SinePcm sine, int blocks, int blockSamples = 1024, int sampleRate = 48000, int channels = 2)
        {
            var pcm = new short[blockSamples * channels];
            fixed (short* p = pcm)
            {
                for (int i = 0; i < blocks; i++)
                {
                    sine.Fill(pcm, blockSamples);
                    recorder.PushAudioFrame(new RawAudioFrame((nint)p, blockSamples, sampleRate, channels, SampleFormat.S16, 0));
                }
            }
        }
    }
}
