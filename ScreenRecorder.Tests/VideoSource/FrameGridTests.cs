using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.VideoSource;

namespace ScreenRecorder.Tests.VideoSource
{
    /// <summary>
    /// The ideal-grid arithmetic behind the paced clock. Every expectation is derived from exact
    /// rational arithmetic (BigInteger), independent of the implementation's split into
    /// whole/remainder terms.
    /// </summary>
    [TestClass]
    public sealed class FrameGridTests
    {
        /// <summary>QPC frequencies: the usual 10 MHz and two that divide nothing evenly.</summary>
        private static readonly long[] Frequencies = { 10_000_000, 3_579_545, 2_441_407 };

        private static readonly long[] Origins = { 0, 1, 123_456_789, 8_765_432_109_876 };

        private static readonly (int Num, int Den)[] Rates =
        {
            (15, 1), (24, 1), (25, 1), (30, 1), (48, 1), (50, 1), (60, 1), (72, 1), (120, 1), (144, 1),
            (30000, 1001), (60000, 1001), (24000, 1001),
        };

        /// <summary>Dense at the start, then geometrically spaced up to ~2 billion (a year at 60 fps).</summary>
        private static IEnumerable<long> SampleIndices()
        {
            for (long k = 0; k <= 3000; k++)
                yield return k;

            for (long k = 3001; k < 2_000_000_000; k = k * 3 + 7)
            {
                yield return k;
                yield return k + 1;
            }
        }

        private static long ExactTicksAt(long origin, int num, int den, long frequency, long index)
        {
            // origin + ceil(index * den * frequency / num)
            BigInteger numerator = new BigInteger(index) * den * frequency;
            BigInteger quotient = BigInteger.DivRem(numerator, num, out BigInteger remainder);
            if (!remainder.IsZero)
                quotient += 1;
            return origin + (long)quotient;
        }

        private static long ExactIndexAt(long origin, int num, int den, long frequency, long now)
        {
            if (now <= origin)
                return 0;

            // floor((now - origin) * num / (den * frequency))
            return (long)(new BigInteger(now - origin) * num / (new BigInteger(den) * frequency));
        }

        [TestMethod]
        [DataRow(15, 1)]
        [DataRow(24, 1)]
        [DataRow(25, 1)]
        [DataRow(30, 1)]
        [DataRow(48, 1)]
        [DataRow(50, 1)]
        [DataRow(60, 1)]
        [DataRow(72, 1)]
        [DataRow(120, 1)]
        [DataRow(144, 1)]
        [DataRow(30000, 1001)]
        [DataRow(60000, 1001)]
        [DataRow(24000, 1001)]
        public void IndexAtTicksAt_RoundTripsExactly(int num, int den)
        {
            foreach (long frequency in Frequencies)
            {
                foreach (long origin in Origins)
                {
                    var grid = new FrameGrid(origin, num, den, frequency);
                    foreach (long k in SampleIndices())
                    {
                        long ticks = grid.TicksAt(k);
                        if (grid.IndexAt(ticks) != k)
                            Assert.Fail($"{num}/{den} fps, f={frequency}, origin={origin}: IndexAt(TicksAt({k})) = {grid.IndexAt(ticks)}.");
                    }
                }
            }
        }

        [TestMethod]
        [DataRow(15, 1)]
        [DataRow(24, 1)]
        [DataRow(25, 1)]
        [DataRow(30, 1)]
        [DataRow(48, 1)]
        [DataRow(50, 1)]
        [DataRow(60, 1)]
        [DataRow(72, 1)]
        [DataRow(120, 1)]
        [DataRow(144, 1)]
        [DataRow(30000, 1001)]
        [DataRow(60000, 1001)]
        [DataRow(24000, 1001)]
        public void IndexAt_OneTickBeforeAGridPoint_IsThePreviousIndex(int num, int den)
        {
            foreach (long frequency in Frequencies)
            {
                foreach (long origin in Origins)
                {
                    var grid = new FrameGrid(origin, num, den, frequency);
                    foreach (long k in SampleIndices())
                    {
                        if (k == 0)
                            continue;

                        long before = grid.IndexAt(grid.TicksAt(k) - 1);
                        if (before != k - 1)
                            Assert.Fail($"{num}/{den} fps, f={frequency}, origin={origin}: IndexAt(TicksAt({k}) - 1) = {before}.");
                    }
                }
            }
        }

        [TestMethod]
        public void TicksAt_MatchesExactCeilingOfTheRationalGrid()
        {
            foreach (var (num, den) in Rates)
            {
                foreach (long frequency in Frequencies)
                {
                    foreach (long origin in Origins)
                    {
                        var grid = new FrameGrid(origin, num, den, frequency);
                        foreach (long k in SampleIndices())
                        {
                            long expected = ExactTicksAt(origin, num, den, frequency, k);
                            if (grid.TicksAt(k) != expected)
                                Assert.Fail($"{num}/{den} fps, f={frequency}, origin={origin}: TicksAt({k}) = {grid.TicksAt(k)}, expected {expected}.");
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void IndexAt_MatchesExactFloorOfTheRationalGrid()
        {
            var random = new Random(20260117);
            foreach (var (num, den) in Rates)
            {
                foreach (long frequency in Frequencies)
                {
                    foreach (long origin in Origins)
                    {
                        var grid = new FrameGrid(origin, num, den, frequency);
                        for (int i = 0; i < 2000; i++)
                        {
                            // Up to ~10 days of elapsed time, spread over all magnitudes.
                            long elapsed = (long)(Math.Pow(random.NextDouble(), 4) * frequency * 864_000) + random.Next(0, 3);
                            long now = origin + elapsed;
                            long expected = ExactIndexAt(origin, num, den, frequency, now);
                            if (grid.IndexAt(now) != expected)
                                Assert.Fail($"{num}/{den} fps, f={frequency}, origin={origin}: IndexAt(origin + {elapsed}) = {grid.IndexAt(now)}, expected {expected}.");
                        }
                    }
                }
            }
        }

        [TestMethod]
        [DataRow(15)]
        [DataRow(24)]
        [DataRow(25)]
        [DataRow(30)]
        [DataRow(48)]
        [DataRow(50)]
        [DataRow(60)]
        [DataRow(72)]
        [DataRow(120)]
        [DataRow(144)]
        public void TicksAt_WholeHours_HaveNoDrift(int fps)
        {
            foreach (long frequency in Frequencies)
            {
                foreach (long origin in Origins)
                {
                    var grid = new FrameGrid(origin, fps, 1, frequency);

                    Assert.AreEqual(origin, grid.TicksAt(0));
                    Assert.AreEqual(origin + frequency, grid.TicksAt(fps), "one second");
                    Assert.AreEqual(origin + 3600 * frequency, grid.TicksAt(fps * 3600L), "one hour");
                    Assert.AreEqual(origin + 24 * 3600 * frequency, grid.TicksAt(fps * 3600L * 24), "one day");
                    Assert.AreEqual(origin + 1000 * 3600 * frequency, grid.TicksAt(fps * 3600L * 1000), "1000 hours");
                }
            }
        }

        [TestMethod]
        [DataRow(30000, 1001)]
        [DataRow(60000, 1001)]
        [DataRow(24000, 1001)]
        public void TicksAt_FractionalRate_WholeCyclesHaveNoDrift(int num, int den)
        {
            foreach (long frequency in Frequencies)
            {
                foreach (long origin in Origins)
                {
                    var grid = new FrameGrid(origin, num, den, frequency);

                    // 'num' frames last exactly 'den' seconds.
                    for (long cycles = 0; cycles <= 3600; cycles += 97)
                        Assert.AreEqual(origin + cycles * den * frequency, grid.TicksAt(cycles * num), $"after {cycles} cycles");
                }
            }
        }

        [TestMethod]
        public void TicksAt_IsStrictlyIncreasing_WithFloorOrCeilSpacing()
        {
            foreach (var (num, den) in Rates)
            {
                foreach (long frequency in Frequencies)
                {
                    var grid = new FrameGrid(777, num, den, frequency);
                    long floor = frequency * den / num;
                    long ceil = (frequency * den + num - 1) / num;

                    long previous = grid.TicksAt(0);
                    for (long k = 1; k <= 20_000; k++)
                    {
                        long current = grid.TicksAt(k);
                        long step = current - previous;
                        if (step != floor && step != ceil)
                            Assert.Fail($"{num}/{den} fps, f={frequency}: step {step} between ticks {k - 1} and {k} (expected {floor} or {ceil}).");
                        previous = current;
                    }
                }
            }
        }

        [TestMethod]
        public void IndexAt_AtOrBeforeTheOrigin_IsZero()
        {
            foreach (var (num, den) in Rates)
            {
                foreach (long frequency in Frequencies)
                {
                    const long origin = 5_000_000_000;
                    var grid = new FrameGrid(origin, num, den, frequency);

                    Assert.AreEqual(0, grid.IndexAt(origin));
                    Assert.AreEqual(0, grid.IndexAt(origin - 1));
                    Assert.AreEqual(0, grid.IndexAt(origin - frequency));
                    Assert.AreEqual(0, grid.IndexAt(0));
                    Assert.AreEqual(0, grid.IndexAt(-origin));
                    Assert.AreEqual(0, grid.IndexAt(origin + 1), "one tick after the origin is still frame 0");
                }
            }
        }

        [TestMethod]
        public void IndexAt_IsMonotonic_AndStepsExactlyOnGridPoints()
        {
            foreach (var (num, den) in Rates)
            {
                foreach (long frequency in Frequencies)
                {
                    const long origin = 42;
                    var grid = new FrameGrid(origin, num, den, frequency);

                    // Walk two seconds of wall clock in small odd steps and keep an independent
                    // count of the grid points passed so far.
                    long expectedIndex = 0;
                    long previousIndex = 0;
                    for (long now = origin; now <= origin + 2 * frequency; now += 911)
                    {
                        while (grid.TicksAt(expectedIndex + 1) <= now)
                            expectedIndex++;

                        long index = grid.IndexAt(now);
                        if (index < previousIndex)
                            Assert.Fail($"{num}/{den} fps, f={frequency}: IndexAt went backwards at {now}.");
                        if (index != expectedIndex)
                            Assert.Fail($"{num}/{den} fps, f={frequency}: IndexAt({now}) = {index}, expected {expectedIndex}.");
                        previousIndex = index;
                    }

                    // Two seconds must have produced two seconds worth of frames.
                    Assert.AreEqual(2L * num / den, grid.IndexAt(origin + 2 * frequency));
                }
            }
        }

        [TestMethod]
        public void IndexAt_LateWakeUp_ReportsTheIndexTheWallClockIsAt()
        {
            // 60 fps on a 10 MHz clock: a wake-up 2.5 frame periods after tick 10 belongs to tick 12.
            var grid = new FrameGrid(1_000, 60, 1, 10_000_000);
            long tick10 = grid.TicksAt(10);
            long period = 10_000_000 / 60;

            Assert.AreEqual(10, grid.IndexAt(tick10));
            Assert.AreEqual(10, grid.IndexAt(tick10 + period / 2));
            Assert.AreEqual(12, grid.IndexAt(tick10 + period * 5 / 2));
        }

        [TestMethod]
        [DataRow(0, 1, "fpsNumerator")]
        [DataRow(-1, 1, "fpsNumerator")]
        [DataRow(int.MinValue, 1, "fpsNumerator")]
        [DataRow(60, 0, "fpsDenominator")]
        [DataRow(60, -1, "fpsDenominator")]
        [DataRow(60, int.MinValue, "fpsDenominator")]
        public void Constructor_NonPositiveRate_Throws(int num, int den, string expectedParam)
        {
            var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new FrameGrid(0, num, den, 10_000_000));
            Assert.AreEqual(expectedParam, ex.ParamName);
        }
    }
}
