using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.Tests.Timing
{
    [TestClass]
    public sealed class AutoFrameRateTests
    {
        [TestMethod]
        public void Constants_AutoIsZeroAndDefaultGridIsSixty()
        {
            Assert.AreEqual(0, AutoFrameRate.Auto);
            Assert.AreEqual(60, AutoFrameRate.DefaultGridRate);
        }

        [TestMethod]
        [DataRow(0, true)]
        [DataRow(-1, true)]
        [DataRow(int.MinValue, true)]
        [DataRow(1, false)]
        [DataRow(15, false)]
        [DataRow(60, false)]
        [DataRow(144, false)]
        [DataRow(int.MaxValue, false)]
        public void IsAuto_NonPositiveRatesAreAutomatic(int configured, bool expected)
        {
            Assert.AreEqual(expected, AutoFrameRate.IsAuto(configured));
        }

        [TestMethod]
        // Unknown / "hardware default".
        [DataRow(0, 60)]
        [DataRow(1, 60)]
        [DataRow(-1, 60)]
        [DataRow(int.MinValue, 60)]
        // NTSC-style rates the driver truncated: rounded up to the nominal rate.
        [DataRow(23, 24)]
        [DataRow(29, 30)]
        [DataRow(47, 48)]
        [DataRow(49, 50)]
        [DataRow(59, 60)]
        [DataRow(71, 72)]
        [DataRow(89, 90)]
        [DataRow(99, 100)]
        [DataRow(119, 120)]
        [DataRow(143, 144)]
        [DataRow(149, 150)]
        [DataRow(179, 180)]
        [DataRow(239, 240)]
        [DataRow(359, 360)]
        // Already nominal: unchanged.
        [DataRow(24, 24)]
        [DataRow(25, 25)]
        [DataRow(30, 30)]
        [DataRow(48, 48)]
        [DataRow(50, 50)]
        [DataRow(60, 60)]
        [DataRow(75, 75)]
        [DataRow(100, 100)]
        [DataRow(120, 120)]
        [DataRow(144, 144)]
        [DataRow(165, 165)]
        [DataRow(240, 240)]
        // Not one below a nominal rate: unchanged.
        [DataRow(2, 2)]
        [DataRow(74, 74)]
        [DataRow(164, 164)]
        public void NormalizeRefreshRate_RoundsTruncatedRatesUp(int refreshHz, int expected)
        {
            Assert.AreEqual(expected, AutoFrameRate.NormalizeRefreshRate(refreshHz));
        }

        [TestMethod]
        public void NormalizeRefreshRate_NeverChangesARateByMoreThanOne()
        {
            for (int hz = 2; hz <= 1000; hz++)
            {
                int normalized = AutoFrameRate.NormalizeRefreshRate(hz);
                Assert.IsTrue(normalized == hz || normalized == hz + 1, $"{hz} Hz normalized to {normalized}.");
            }
        }

        [TestMethod]
        public void NormalizeRefreshRate_IsIdempotent()
        {
            for (int hz = -5; hz <= 1000; hz++)
            {
                int once = AutoFrameRate.NormalizeRefreshRate(hz);
                Assert.AreEqual(once, AutoFrameRate.NormalizeRefreshRate(once), $"Normalizing {hz} Hz twice changed the result.");
            }
        }

        [TestMethod]
        // The documented table.
        [DataRow(60, 60)]
        [DataRow(75, 75)]
        [DataRow(120, 60)]
        [DataRow(144, 72)]
        [DataRow(165, 55)]
        [DataRow(240, 60)]
        [DataRow(30, 30)]
        // Unknown refresh rate.
        [DataRow(0, 60)]
        [DataRow(1, 60)]
        [DataRow(-1, 60)]
        // Truncated NTSC-style rates behave like their nominal rate.
        [DataRow(59, 60)]
        [DataRow(119, 60)]
        [DataRow(143, 72)]
        [DataRow(239, 60)]
        [DataRow(23, 24)]
        [DataRow(89, 90)]
        // Up to 90 Hz the grid follows the monitor, but never below 15 fps.
        [DataRow(24, 24)]
        [DataRow(50, 50)]
        [DataRow(90, 90)]
        [DataRow(15, 15)]
        [DataRow(14, 15)]
        [DataRow(10, 15)]
        [DataRow(2, 15)]
        // Above 90 Hz: the divisor that lands nearest 60 fps.
        [DataRow(91, 46)]
        [DataRow(100, 50)]
        [DataRow(150, 50)]
        [DataRow(175, 58)]
        [DataRow(180, 60)]
        [DataRow(200, 67)]
        [DataRow(360, 60)]
        [DataRow(480, 60)]
        [DataRow(500, 63)]
        [DataRow(1000, 59)]
        public void FromRefreshRate_PicksAGridNearSixty(int refreshHz, int expected)
        {
            Assert.AreEqual(expected, AutoFrameRate.FromRefreshRate(refreshHz));
        }

        [TestMethod]
        public void FromRefreshRate_AlwaysWithinSupportedRange()
        {
            for (int hz = -10; hz <= 2000; hz++)
            {
                int rate = AutoFrameRate.FromRefreshRate(hz);
                Assert.IsTrue(rate >= 15 && rate <= 90, $"{hz} Hz gave a grid of {rate} fps.");

                if (hz > 90)
                    Assert.IsTrue(rate >= 30, $"{hz} Hz gave a grid of only {rate} fps.");
            }
        }

        [TestMethod]
        public void FromRefreshRate_HighRefreshGridDividesTheRefreshRate()
        {
            // Above 90 Hz the grid is refresh / n for a whole n, so each grid tick samples a
            // whole number of monitor frames (within the rounding of non-divisible rates).
            foreach (int hz in new[] { 100, 120, 144, 165, 180, 200, 240, 360, 480 })
            {
                int rate = AutoFrameRate.FromRefreshRate(hz);
                double divisor = hz / (double)rate;
                double nearest = System.Math.Round(divisor);
                Assert.IsTrue(nearest >= 2, $"{hz} Hz → {rate} fps is not a division.");
                Assert.IsTrue(System.Math.Abs(divisor - nearest) < 0.02, $"{hz} Hz → {rate} fps is not close to a whole divisor ({divisor:0.###}).");
            }
        }

        [TestMethod]
        [DataRow(30, 144, 30)]
        [DataRow(60, 0, 60)]
        [DataRow(144, 60, 144)]
        [DataRow(15, 240, 15)]
        [DataRow(1, 60, 1)]
        public void ResolveGridRate_FixedRateIgnoresTheMonitor(int configured, int refreshHz, int expected)
        {
            Assert.AreEqual(expected, AutoFrameRate.ResolveGridRate(configured, refreshHz));
        }

        [TestMethod]
        [DataRow(0, 60, 60)]
        [DataRow(0, 144, 72)]
        [DataRow(0, 165, 55)]
        [DataRow(0, 0, 60)]
        [DataRow(0, 59, 60)]
        [DataRow(-1, 240, 60)]
        [DataRow(-1, 75, 75)]
        public void ResolveGridRate_AutoDerivesFromTheMonitor(int configured, int refreshHz, int expected)
        {
            Assert.AreEqual(expected, AutoFrameRate.ResolveGridRate(configured, refreshHz));
        }
    }
}
