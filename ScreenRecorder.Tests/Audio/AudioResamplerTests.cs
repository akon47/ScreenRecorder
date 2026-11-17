using System;
using FFmpeg.AutoGen;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Tests.Support;

namespace ScreenRecorder.Tests.Audio
{
    [TestClass]
    [TestCategory("Integration")]
    public sealed unsafe class AudioResamplerTests
    {
        private const int Block = 1024;

        [TestInitialize]
        public void RequireFFmpeg() => FFmpegTestEnvironment.RequireFFmpeg();

        /// <summary>Pushes <paramref name="blocks"/> sine blocks through and returns the total output samples.</summary>
        private static long Run(AudioResampler resampler, int inRate, int blocks, Action<int> beforeBlock = null)
        {
            var sine = new SinePcm(inRate, 2);
            var pcm = new short[Block * 2];
            long total = 0;

            fixed (short* p = pcm)
            {
                byte** planes = stackalloc byte*[1];
                planes[0] = (byte*)p;

                for (int i = 0; i < blocks; i++)
                {
                    beforeBlock?.Invoke(i);
                    sine.Fill(pcm, Block);
                    int produced = resampler.Resample(planes, Block);
                    Assert.IsTrue(produced >= 0);
                    total += produced;
                }
            }

            return total;
        }

        [TestMethod]
        public void Properties_ReportTheInputFormat()
        {
            using var resampler = new AudioResampler(SampleFormat.S16, 44100, 2, AVSampleFormat.AV_SAMPLE_FMT_FLT, 48000, 2);

            Assert.AreEqual(44100, resampler.InRate);
            Assert.AreEqual(2, resampler.InChannels);
        }

        [TestMethod]
        public void SameRate_FormatConversionOnly_IsSampleExact()
        {
            using var resampler = new AudioResampler(SampleFormat.S16, 48000, 2, AVSampleFormat.AV_SAMPLE_FMT_FLT, 48000, 2);
            var pcm = new short[Block * 2];
            new SinePcm(48000, 2).Fill(pcm, Block);

            fixed (short* p = pcm)
            {
                byte** planes = stackalloc byte*[1];
                planes[0] = (byte*)p;

                int produced = resampler.Resample(planes, Block);

                Assert.AreEqual(Block, produced, "no rate change: one sample out per sample in");
                float* output = (float*)resampler.GetOutputPlane(0);
                for (int i = 0; i < Block * 2; i++)
                    Assert.AreEqual(pcm[i] / 32768f, output[i], 1e-6f, $"sample {i}");
            }
        }

        [TestMethod]
        public void MonoToStereo_DuplicatesTheChannel()
        {
            // The 16 kHz mono microphone case, without the rate change: both output channels
            // carry the input.
            using var resampler = new AudioResampler(SampleFormat.FLT, 48000, 1, AVSampleFormat.AV_SAMPLE_FMT_FLT, 48000, 2);
            var mono = new float[Block];
            for (int i = 0; i < Block; i++)
                mono[i] = (float)Math.Sin(i * 0.05) * 0.5f;

            fixed (float* p = mono)
            {
                byte** planes = stackalloc byte*[1];
                planes[0] = (byte*)p;

                int produced = resampler.Resample(planes, Block);

                Assert.AreEqual(Block, produced);
                float* output = (float*)resampler.GetOutputPlane(0);
                double energyIn = 0, energyLeft = 0, energyRight = 0;
                for (int i = 0; i < Block; i++)
                {
                    Assert.AreEqual(output[i * 2], output[i * 2 + 1], 1e-6f, $"left and right differ at {i}");
                    energyIn += mono[i] * mono[i];
                    energyLeft += output[i * 2] * output[i * 2];
                    energyRight += output[i * 2 + 1] * output[i * 2 + 1];
                }

                Assert.IsTrue(energyLeft > energyIn * 0.2, "the up-mixed channels are (nearly) silent");
                Assert.IsTrue(energyRight > energyIn * 0.2);
            }
        }

        [TestMethod]
        public void Upsample_44100To48000_ProducesTheExpectedSampleCount()
        {
            using var resampler = new AudioResampler(SampleFormat.S16, 44100, 2, AVSampleFormat.AV_SAMPLE_FMT_FLT, 48000, 2);
            const int blocks = 500;

            long total = Run(resampler, 44100, blocks);

            double expected = blocks * (double)Block * 48000 / 44100; // 557 278.9
            double oneOutputBlock = Block * 48000.0 / 44100;
            Assert.AreEqual(expected, total, oneOutputBlock, "output sample count");
            Assert.IsTrue(total <= expected + 1, "the resampler cannot produce samples it has no input for");
        }

        [TestMethod]
        public void Downsample_96000To48000_ProducesHalfTheSamples()
        {
            using var resampler = new AudioResampler(SampleFormat.S16, 96000, 2, AVSampleFormat.AV_SAMPLE_FMT_FLT, 48000, 2);
            const int blocks = 400;

            long total = Run(resampler, 96000, blocks);

            Assert.AreEqual(blocks * Block / 2.0, total, Block / 2.0);
        }

        [TestMethod]
        public void Upsample_PreservesTheSignalLevel()
        {
            using var resampler = new AudioResampler(SampleFormat.S16, 44100, 2, AVSampleFormat.AV_SAMPLE_FMT_FLT, 48000, 2);
            var sine = new SinePcm(44100, 2);
            var pcm = new short[Block * 2];
            double sumSquares = 0;
            double peak = 0;
            long count = 0;

            fixed (short* p = pcm)
            {
                byte** planes = stackalloc byte*[1];
                planes[0] = (byte*)p;

                for (int block = 0; block < 100; block++)
                {
                    sine.Fill(pcm, Block);
                    int produced = resampler.Resample(planes, Block);
                    if (block < 5)
                        continue; // skip the filter's start-up transient

                    float* output = (float*)resampler.GetOutputPlane(0);
                    for (int i = 0; i < produced * 2; i++)
                    {
                        sumSquares += output[i] * (double)output[i];
                        peak = Math.Max(peak, Math.Abs(output[i]));
                        count++;
                    }
                }
            }

            double rms = Math.Sqrt(sumSquares / count);
            Assert.AreEqual(SinePcm.Amplitude / Math.Sqrt(2), rms, 0.01, "RMS of a 0.3 amplitude sine");
            Assert.AreEqual(SinePcm.Amplitude, peak, 0.02, "peak");
        }

        [TestMethod]
        public void RateCompensation_ZeroPpm_KeepsTheNominalRatio()
        {
            using var resampler = new AudioResampler(SampleFormat.S16, 48000, 2, AVSampleFormat.AV_SAMPLE_FMT_FLT, 48000, 2, enableRateCompensation: true);
            const int blocks = 2000;

            long total = Run(resampler, 48000, blocks);

            // The resampling stage is forced on, so a few samples stay inside the filter.
            long input = (long)blocks * Block;
            Assert.IsTrue(total <= input, $"{total} samples out of {input} in");
            Assert.IsTrue(total >= input - 256, $"{input - total} samples missing at 0 ppm");
        }

        [TestMethod]
        [DataRow(48000)]
        [DataRow(44100)]
        public void RateCompensation_PlusMinus1000Ppm_ShiftsTheOutputCountByATenthOfAPercent(int inRate)
        {
            const int blocks = 3000; // ~3 million input samples
            long Total(int ppm)
            {
                using var resampler = new AudioResampler(SampleFormat.S16, inRate, 2, AVSampleFormat.AV_SAMPLE_FMT_FLT, 48000, 2, enableRateCompensation: true);
                resampler.SetCompensation(ppm);
                return Run(resampler, inRate, blocks);
            }

            long nominal = Total(0);
            long faster = Total(+1000);
            long slower = Total(-1000);

            double expectedShift = nominal * 0.001;
            Assert.IsTrue(faster > nominal, $"+1000 ppm produced {faster} samples, nominal {nominal}: positive must mean MORE output");
            Assert.IsTrue(slower < nominal, $"-1000 ppm produced {slower} samples, nominal {nominal}: negative must mean FEWER output");
            Assert.AreEqual(expectedShift, faster - nominal, expectedShift * 0.25, "+1000 ppm shift");
            Assert.AreEqual(expectedShift, nominal - slower, expectedShift * 0.25, "-1000 ppm shift");
        }

        [TestMethod]
        public void RateCompensation_SmallCorrection_IsProportional()
        {
            const int blocks = 3000;
            long Total(int ppm)
            {
                using var resampler = new AudioResampler(SampleFormat.S16, 48000, 2, AVSampleFormat.AV_SAMPLE_FMT_FLT, 48000, 2, enableRateCompensation: true);
                resampler.SetCompensation(ppm);
                return Run(resampler, 48000, blocks);
            }

            long nominal = Total(0);
            long plus100 = Total(100);

            // 100 ppm of 3 072 000 samples = 307 samples.
            Assert.AreEqual(nominal * 0.0001, plus100 - nominal, 80, "+100 ppm shift");
        }

        [TestMethod]
        public void RateCompensation_BackToZero_RestoresTheNominalRatio()
        {
            using var resampler = new AudioResampler(SampleFormat.S16, 48000, 2, AVSampleFormat.AV_SAMPLE_FMT_FLT, 48000, 2, enableRateCompensation: true);

            resampler.SetCompensation(1000);
            long corrected = Run(resampler, 48000, 1000);

            resampler.SetCompensation(0);
            long nominal = Run(resampler, 48000, 1000);

            long input = 1000L * Block;
            Assert.IsTrue(corrected > input + 500, $"+1000 ppm phase produced {corrected} of {input}");
            Assert.AreEqual(input, nominal, 64, "after returning to 0 ppm the ratio must be 1:1 again");
        }

        [TestMethod]
        public void SetCompensation_WithoutRateCompensation_HasNoEffect()
        {
            using var resampler = new AudioResampler(SampleFormat.S16, 48000, 2, AVSampleFormat.AV_SAMPLE_FMT_FLT, 48000, 2);
            resampler.SetCompensation(1000);

            long total = Run(resampler, 48000, 500);

            Assert.AreEqual(500L * Block, total);
        }

        [TestMethod]
        public void Dispose_Twice_IsHarmless()
        {
            var resampler = new AudioResampler(SampleFormat.S16, 44100, 2, AVSampleFormat.AV_SAMPLE_FMT_FLT, 48000, 2);
            Run(resampler, 44100, 4);

            resampler.Dispose();
            resampler.Dispose();
        }
    }
}
