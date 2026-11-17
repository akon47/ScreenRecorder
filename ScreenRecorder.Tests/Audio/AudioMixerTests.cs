using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.AudioSource;

namespace ScreenRecorder.Tests.Audio
{
    [TestClass]
    public sealed class AudioMixerTests
    {
        private const short Garbage = 12345;

        /// <summary>A timeline holding the given interleaved stereo samples.</summary>
        private static SourceTimeline Timeline(params float[] interleaved)
        {
            var timeline = new SourceTimeline(4096);
            timeline.Write(interleaved, interleaved.Length / 2);
            return timeline;
        }

        private static short[] Mix(AudioMixer mixer, int frames)
        {
            var output = new short[frames * 2];
            Array.Fill(output, Garbage);
            mixer.MixBlock(output, frames);
            return output;
        }

        #region ToS16

        [TestMethod]
        [DataRow(0f, (short)0)]
        [DataRow(1f, (short)32767)]
        [DataRow(-1f, (short)-32767)]
        [DataRow(0.5f, (short)16383)]
        [DataRow(-0.5f, (short)-16383)]
        [DataRow(0.25f, (short)8191)]
        [DataRow(0.75f, (short)24575)]
        [DataRow(0.125f, (short)4095)]
        [DataRow(-0.75f, (short)-24575)]
        public void ToS16_ScalesFullScaleToPlusMinus32767(float sample, short expected)
        {
            Assert.AreEqual(expected, AudioMixer.ToS16(sample));
        }

        [TestMethod]
        [DataRow(1.0001f, (short)32767)]
        [DataRow(2f, (short)32767)]
        [DataRow(1000f, (short)32767)]
        [DataRow(float.MaxValue, (short)32767)]
        [DataRow(float.PositiveInfinity, (short)32767)]
        [DataRow(-1.0001f, (short)-32767)]
        [DataRow(-2f, (short)-32767)]
        [DataRow(float.MinValue, (short)-32767)]
        [DataRow(float.NegativeInfinity, (short)-32767)]
        public void ToS16_OutOfRange_IsClamped(float sample, short expected)
        {
            Assert.AreEqual(expected, AudioMixer.ToS16(sample));
        }

        [TestMethod]
        public void ToS16_NaN_IsSilence()
        {
            Assert.AreEqual((short)0, AudioMixer.ToS16(float.NaN));
        }

        [TestMethod]
        public void ToS16_IsSymmetricAndMonotonic()
        {
            short previous = short.MinValue;
            for (int i = -2000; i <= 2000; i++)
            {
                float sample = i / 1000f;
                short value = AudioMixer.ToS16(sample);

                Assert.IsTrue(value >= previous, $"ToS16 is not monotonic at {sample}");
                Assert.AreEqual(-value, AudioMixer.ToS16(-sample), $"ToS16 is not symmetric at {sample} (DC bias)");
                previous = value;
            }
        }

        #endregion

        #region Sources

        [TestMethod]
        public void NoSources_ProducesSilence()
        {
            var mixer = new AudioMixer(null, null);

            short[] output = Mix(mixer, 8);

            Assert.IsTrue(output.All(s => s == 0));
        }

        [TestMethod]
        public void NoSources_ClearsOnlyTheRequestedBlock()
        {
            var mixer = new AudioMixer(null, null);
            var output = new short[20];
            Array.Fill(output, Garbage);

            mixer.MixBlock(output, 4);

            Assert.IsTrue(output.Take(8).All(s => s == 0));
            Assert.IsTrue(output.Skip(8).All(s => s == Garbage), "samples beyond the block were touched");
        }

        [TestMethod]
        public void LoopbackOnly_PassesTheSourceThrough()
        {
            var loopback = Timeline(0.5f, -0.5f, 0.25f, -0.25f, 1f, -1f);
            var mixer = new AudioMixer(loopback, null);

            short[] output = Mix(mixer, 3);

            CollectionAssert.AreEqual(new short[] { 16383, -16383, 8191, -8191, 32767, -32767 }, output);
            Assert.AreEqual(0, loopback.AvailableFrames, "the block was consumed from the timeline");
        }

        [TestMethod]
        public void MicrophoneOnly_PassesTheSourceThrough()
        {
            var mic = Timeline(0.5f, -0.5f, 0.25f, -0.25f);
            var mixer = new AudioMixer(null, mic);

            short[] output = Mix(mixer, 2);

            CollectionAssert.AreEqual(new short[] { 16383, -16383, 8191, -8191 }, output);
            Assert.AreEqual(0, mic.AvailableFrames);
        }

        [TestMethod]
        public void TwoSources_AreSummedSampleBySample()
        {
            var loopback = Timeline(0.25f, 0.5f, -0.25f, 0f);
            var mic = Timeline(0.5f, -0.25f, -0.5f, 0.125f);
            var mixer = new AudioMixer(loopback, mic);

            short[] output = Mix(mixer, 2);

            // 0.75, 0.25, -0.75, 0.125 of full scale.
            CollectionAssert.AreEqual(new short[] { 24575, 8191, -24575, 4095 }, output);
            Assert.AreEqual(0, loopback.AvailableFrames);
            Assert.AreEqual(0, mic.AvailableFrames);
        }

        [TestMethod]
        public void TwoSources_OppositeSignals_Cancel()
        {
            var loopback = Timeline(0.6f, -0.3f);
            var mic = Timeline(-0.6f, 0.3f);
            var mixer = new AudioMixer(loopback, mic);

            CollectionAssert.AreEqual(new short[] { 0, 0 }, Mix(mixer, 1));
        }

        [TestMethod]
        public void TwoSources_SumBeyondFullScale_IsClampedNotWrapped()
        {
            var loopback = Timeline(0.8f, -0.8f, 1f, -1f);
            var mic = Timeline(0.7f, -0.7f, 1f, -1f);
            var mixer = new AudioMixer(loopback, mic);

            CollectionAssert.AreEqual(new short[] { 32767, -32767, 32767, -32767 }, Mix(mixer, 2));
        }

        [TestMethod]
        public void SingleSource_OutOfRangeSamples_AreClamped()
        {
            var mixer = new AudioMixer(Timeline(1.5f, -1.5f), null);

            CollectionAssert.AreEqual(new short[] { 32767, -32767 }, Mix(mixer, 1));
        }

        [TestMethod]
        public void NaNSamples_BecomeSilence()
        {
            var single = new AudioMixer(Timeline(float.NaN, 0.5f), null);
            CollectionAssert.AreEqual(new short[] { 0, 16383 }, Mix(single, 1));

            // NaN in either source poisons the sum, which must still come out as silence.
            var mixed = new AudioMixer(Timeline(float.NaN, 0.25f), Timeline(0.5f, float.NaN));
            CollectionAssert.AreEqual(new short[] { 0, 0 }, Mix(mixed, 1));
        }

        [TestMethod]
        public void SourceShorterThanTheBlock_RestIsSilence()
        {
            var loopback = Timeline(0.5f, 0.5f); // one frame only
            var mixer = new AudioMixer(loopback, null);

            short[] output = Mix(mixer, 4);

            CollectionAssert.AreEqual(new short[] { 16383, 16383, 0, 0, 0, 0, 0, 0 }, output);
            Assert.AreEqual(3, loopback.UnderrunFrames);
        }

        [TestMethod]
        public void OneSourceIdle_TheOtherIsUnaffected()
        {
            // Loopback delivers nothing while no audio plays; the microphone must still be heard.
            var loopback = new SourceTimeline(4096);
            var mic = Timeline(0.5f, -0.5f, 0.25f, -0.25f);
            var mixer = new AudioMixer(loopback, mic);

            CollectionAssert.AreEqual(new short[] { 16383, -16383, 8191, -8191 }, Mix(mixer, 2));
        }

        [TestMethod]
        public void SourcesOfDifferentLength_EachContributesWhatItHas()
        {
            var loopback = Timeline(0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f); // 3 frames
            var mic = Timeline(0.5f, 0.5f);                                   // 1 frame
            var mixer = new AudioMixer(loopback, mic);

            CollectionAssert.AreEqual(new short[] { 24575, 24575, 8191, 8191, 8191, 8191, 0, 0 }, Mix(mixer, 4));
        }

        [TestMethod]
        public void ConsecutiveBlocks_ContinueTheStream_AndBlockSizeMayChange()
        {
            var samples = new float[2 * 600];
            for (int i = 0; i < 600; i++)
            {
                samples[i * 2] = i / 1000f;
                samples[i * 2 + 1] = -i / 1000f;
            }

            var mixer = new AudioMixer(Timeline(samples), null);

            short[] first = Mix(mixer, 16);    // small block first
            short[] second = Mix(mixer, 512);  // larger: the scratch buffer has to grow
            short[] third = Mix(mixer, 72);

            short[] all = first.Concat(second).Concat(third).ToArray();
            for (int i = 0; i < 600; i++)
            {
                Assert.AreEqual(AudioMixer.ToS16(i / 1000f), all[i * 2], $"left of frame {i}");
                Assert.AreEqual(AudioMixer.ToS16(-i / 1000f), all[i * 2 + 1], $"right of frame {i}");
            }
        }

        [TestMethod]
        public void SilentSources_ProduceExactDigitalSilence()
        {
            // No DC offset: silence in is all-zero S16 out (the legacy mixer biased it by -32768).
            var mixer = new AudioMixer(Timeline(new float[64]), Timeline(new float[64]));

            Assert.IsTrue(Mix(mixer, 32).All(s => s == 0));
        }

        [TestMethod]
        public void SteadyState_MixBlockAllocatesNothing()
        {
            var loopback = new SourceTimeline(4096);
            var mic = new SourceTimeline(4096);
            var mixer = new AudioMixer(loopback, mic);
            var block = new float[1024 * 2];
            var output = new short[1024 * 2];

            for (int i = 0; i < 50; i++)
            {
                loopback.Write(block, 1024);
                mic.Write(block, 1024);
                mixer.MixBlock(output, 1024);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++)
            {
                loopback.Write(block, 1024);
                mic.Write(block, 1024);
                mixer.MixBlock(output, 1024);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated, "the audio tick must not allocate");
        }

        #endregion
    }
}
