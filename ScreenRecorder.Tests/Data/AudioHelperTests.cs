using System.Diagnostics;
using System.Numerics;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ScreenRecorder.Tests.Data
{
    /// <summary>
    /// Sample-count ↔ QPC-tick conversions. Expectations are computed from
    /// <see cref="Stopwatch.Frequency"/> so the tests hold on any machine.
    /// </summary>
    [TestClass]
    public sealed class AudioHelperTests
    {
        private static readonly long F = Stopwatch.Frequency;

        private static readonly int[] SampleRates = { 8000, 16000, 22050, 44100, 48000, 96000, 192000 };

        private static long ExactDuration(long samples, int sampleRate) => (long)(new BigInteger(samples) * F / sampleRate);

        private static long ExactSamples(long ticks, int sampleRate) => (long)(new BigInteger(ticks) * sampleRate / F);

        [TestMethod]
        public void GetDurationFromSamples_Zero_IsZero()
        {
            foreach (int rate in SampleRates)
                Assert.AreEqual(0, AudioHelper.GetDurationFromSamples(0, rate));
        }

        [TestMethod]
        public void GetDurationFromSamples_WholeSeconds_AreExact()
        {
            foreach (int rate in SampleRates)
            {
                Assert.AreEqual(F, AudioHelper.GetDurationFromSamples(rate, rate), $"1 s at {rate} Hz");
                Assert.AreEqual(3600 * F, AudioHelper.GetDurationFromSamples(3600L * rate, rate), $"1 h at {rate} Hz");
                Assert.AreEqual(1000 * 3600 * F, AudioHelper.GetDurationFromSamples(1000L * 3600 * rate, rate), $"1000 h at {rate} Hz");
            }
        }

        [TestMethod]
        public void GetDurationFromSamples_MatchesExactFloor()
        {
            foreach (int rate in SampleRates)
            {
                foreach (long samples in new long[] { 1, 2, 441, 480, 1023, 1024, 1025, rate - 1, rate + 1, 123_456_789, 98_765_432_101 })
                    Assert.AreEqual(ExactDuration(samples, rate), AudioHelper.GetDurationFromSamples(samples, rate), $"{samples} samples at {rate} Hz");
            }
        }

        [TestMethod]
        public void GetDurationFromSamples_BlockCadence_NeverDrifts()
        {
            // The audio push loop wakes at t0 + duration(n * 1024): after 24 hours of blocks the
            // target is still within one tick of the true time (no accumulated rounding).
            const int rate = 48000;
            const int block = 1024;
            long blocksPerDay = 24L * 3600 * rate / block;

            long previous = -1;
            for (long n = 0; n <= blocksPerDay; n += 997)
            {
                long ticks = AudioHelper.GetDurationFromSamples(n * block, rate);
                Assert.AreEqual(ExactDuration(n * block, rate), ticks, $"block {n}");
                Assert.IsTrue(ticks > previous, "the cadence must be strictly increasing");
                previous = ticks;
            }
        }

        [TestMethod]
        public void GetSamplesFromTimeStamp_WholeSeconds_AreExact()
        {
            foreach (int rate in SampleRates)
            {
                Assert.AreEqual(0, AudioHelper.GetSamplesFromTimeStamp(0, rate));
                Assert.AreEqual(rate, AudioHelper.GetSamplesFromTimeStamp(F, rate));
                Assert.AreEqual(3600L * rate, AudioHelper.GetSamplesFromTimeStamp(3600 * F, rate));
                Assert.AreEqual(1000L * 3600 * rate, AudioHelper.GetSamplesFromTimeStamp(1000 * 3600 * F, rate));
            }
        }

        [TestMethod]
        public void GetSamplesFromTimeStamp_MatchesExactFloor()
        {
            foreach (int rate in SampleRates)
            {
                foreach (long ticks in new long[] { 1, F / 1000, F / 100, F / 2, F - 1, F + 1, 7 * F / 3, 123_456_789_012, 86_400 * F + 12_345 })
                    Assert.AreEqual(ExactSamples(ticks, rate), AudioHelper.GetSamplesFromTimeStamp(ticks, rate), $"{ticks} ticks at {rate} Hz");
            }
        }

        [TestMethod]
        public void GetSamplesFromTimeStamp_TenMilliseconds_IsOneDevicePeriod()
        {
            if (F % 100 != 0)
                Assert.Inconclusive("The QPC frequency of this machine has no whole 10 ms.");

            Assert.AreEqual(480, AudioHelper.GetSamplesFromTimeStamp(F / 100, 48000));
            Assert.AreEqual(441, AudioHelper.GetSamplesFromTimeStamp(F / 100, 44100));
        }

        [TestMethod]
        public void RoundTrip_SamplesToTicksToSamples_LosesAtMostOneSample()
        {
            foreach (int rate in SampleRates)
            {
                for (long samples = 0; samples < 200_000; samples += 37)
                {
                    long back = AudioHelper.GetSamplesFromTimeStamp(AudioHelper.GetDurationFromSamples(samples, rate), rate);

                    // Both directions truncate, so the result is the same sample or the one before
                    // — and exactly the same whenever the sample falls on a whole tick.
                    Assert.IsTrue(back == samples || back == samples - 1, $"{samples} samples at {rate} Hz came back as {back}");
                    if (samples * F % rate == 0)
                        Assert.AreEqual(samples, back, $"{samples} samples at {rate} Hz");
                }
            }
        }
    }
}
