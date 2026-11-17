using System.Linq;
using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.DirectX;

namespace ScreenRecorder.Tests.DirectX
{
    /// <summary>
    /// DPI-virtualized ↔ physical coordinate mapping (GitHub issue #58). The cases of the
    /// harness's <c>--dpi-map</c> self-test are ported here, plus more.
    /// </summary>
    [TestClass]
    public sealed class MonitorInfoTests
    {
        /// <summary>3840x2160 at 200%: a DPI-unaware process sees 1920x1080.</summary>
        private static MonitorInfo Monitor200() => new MonitorInfo { Right = 1920, Bottom = 1080, PhysicalWidth = 3840, PhysicalHeight = 2160 };

        /// <summary>2560x1440 at 150%: Windows virtualizes to 1707x960 (a rounded, inexact ratio).</summary>
        private static MonitorInfo Monitor150() => new MonitorInfo { Right = 1707, Bottom = 960, PhysicalWidth = 2560, PhysicalHeight = 1440 };

        /// <summary>1920x1080 at 125%: seen as 1536x864.</summary>
        private static MonitorInfo Monitor125() => new MonitorInfo { Right = 1536, Bottom = 864, PhysicalWidth = 1920, PhysicalHeight = 1080 };

        /// <summary>2560x1440 at 100% (or a DPI-aware process): both spaces are identical.</summary>
        private static MonitorInfo Monitor100() => new MonitorInfo { Right = 2560, Bottom = 1440, PhysicalWidth = 2560, PhysicalHeight = 1440 };

        /// <summary>
        /// A 200% monitor to the right of a 100% primary: virtual (2560,0) 1280x720, physical
        /// (2560,0) 2560x1440.
        /// </summary>
        private static MonitorInfo SecondaryRight200() => new MonitorInfo
        {
            Left = 2560, Top = 0, Right = 3840, Bottom = 720,
            PhysicalLeft = 2560, PhysicalTop = 0, PhysicalWidth = 2560, PhysicalHeight = 1440,
        };

        /// <summary>
        /// A 150% monitor to the left of the primary: virtual (-1707,0) 1707x960, physical
        /// (-2560,0) 2560x1440.
        /// </summary>
        private static MonitorInfo SecondaryLeft150() => new MonitorInfo
        {
            Left = -1707, Top = 0, Right = 0, Bottom = 960,
            PhysicalLeft = -2560, PhysicalTop = 0, PhysicalWidth = 2560, PhysicalHeight = 1440,
        };

        #region Properties

        [TestMethod]
        public void SizeAndBounds_AreDerivedFromTheEdges()
        {
            var monitor = new MonitorInfo
            {
                Left = -1920, Top = 100, Right = 0, Bottom = 1180,
                PhysicalLeft = -3840, PhysicalTop = 200, PhysicalWidth = 3840, PhysicalHeight = 2160,
            };

            Assert.AreEqual(1920, monitor.Width);
            Assert.AreEqual(1080, monitor.Height);
            Assert.AreEqual(new Rect(-1920, 100, 1920, 1080), monitor.Bounds);
            Assert.AreEqual(new Rect(-3840, 200, 3840, 2160), monitor.PhysicalBounds);
        }

        [TestMethod]
        public void Description_ShowsThePhysicalResolutionAndVirtualOrigin()
        {
            var monitor = new MonitorInfo
            {
                AdapterDescription = "Display Adapter",
                Left = 1920, Top = 0, Right = 3840, Bottom = 1080,
                PhysicalWidth = 3840, PhysicalHeight = 2160,
            };

            Assert.AreEqual("Display Adapter: 3840x2160 @ 1920,0", monitor.Description);

            monitor.IsPrimary = true;
            Assert.AreEqual("Display Adapter: 3840x2160 @ 1920,0 (Primary)", monitor.Description);
        }

        #endregion

        #region VirtualToPhysical — cases ported from the harness (--dpi-map)

        [TestMethod]
        public void VirtualToPhysical_200Percent_FullScreen_IsTheWholePhysicalSurface()
        {
            Assert.AreEqual(new Rect(0, 0, 3840, 2160), Monitor200().VirtualToPhysical(new Rect(0, 0, 1920, 1080)));
        }

        [TestMethod]
        public void VirtualToPhysical_200Percent_SubRegion_IsDoubled()
        {
            Assert.AreEqual(new Rect(200, 100, 1280, 720), Monitor200().VirtualToPhysical(new Rect(100, 50, 640, 360)));
        }

        [TestMethod]
        public void VirtualToPhysical_EmptyRegion_StaysEmpty()
        {
            Assert.AreEqual(Rect.Empty, Monitor200().VirtualToPhysical(Rect.Empty));
            Assert.AreEqual(Rect.Empty, Monitor100().VirtualToPhysical(Rect.Empty));
        }

        [TestMethod]
        public void VirtualToPhysical_150Percent_FullScreen_IsExactDespiteTheRoundedVirtualSize()
        {
            Assert.AreEqual(new Rect(0, 0, 2560, 1440), Monitor150().VirtualToPhysical(new Rect(0, 0, 1707, 960)));
        }

        [TestMethod]
        public void VirtualToPhysical_150Percent_SubRegionAtTheFarEdge_StaysInBounds()
        {
            Assert.AreEqual(new Rect(1500, 750, 1060, 690), Monitor150().VirtualToPhysical(new Rect(1000, 500, 707, 460)));
        }

        [TestMethod]
        public void VirtualToPhysical_100Percent_IsIdentity()
        {
            Assert.AreEqual(new Rect(0, 0, 2560, 1440), Monitor100().VirtualToPhysical(new Rect(0, 0, 2560, 1440)));
            Assert.AreEqual(new Rect(123, 45, 640, 480), Monitor100().VirtualToPhysical(new Rect(123, 45, 640, 480)));
        }

        #endregion

        #region VirtualToPhysical — additional cases

        [TestMethod]
        public void VirtualToPhysical_100Percent_DoesNotEvenClip()
        {
            // No-op means no-op: the caller has already intersected with the monitor.
            var oversize = new Rect(-10, -10, 5000, 5000);

            Assert.AreEqual(oversize, Monitor100().VirtualToPhysical(oversize));
        }

        [TestMethod]
        public void VirtualToPhysical_125Percent_FullScreenAndSubRegion()
        {
            Assert.AreEqual(new Rect(0, 0, 1920, 1080), Monitor125().VirtualToPhysical(new Rect(0, 0, 1536, 864)));
            Assert.AreEqual(new Rect(125, 250, 500, 375), Monitor125().VirtualToPhysical(new Rect(100, 200, 400, 300)));
        }

        [TestMethod]
        public void VirtualToPhysical_RegionLargerThanTheMonitor_IsTheWholePhysicalSurface()
        {
            // "Record the full screen" is expressed as an oversized region.
            Assert.AreEqual(new Rect(0, 0, 3840, 2160), Monitor200().VirtualToPhysical(new Rect(0, 0, double.MaxValue, double.MaxValue)));
            Assert.AreEqual(new Rect(0, 0, 3840, 2160), Monitor200().VirtualToPhysical(new Rect(-50, -50, 5000, 5000)));
            Assert.AreEqual(new Rect(0, 0, 2560, 1440), Monitor150().VirtualToPhysical(new Rect(0, 0, 100_000, 100_000)));
        }

        [TestMethod]
        public void VirtualToPhysical_FullWidthPartialHeight_IsScaledNotSnappedToFullScreen()
        {
            Assert.AreEqual(new Rect(0, 0, 3840, 1080), Monitor200().VirtualToPhysical(new Rect(0, 0, 1920, 540)));
            Assert.AreEqual(new Rect(0, 1080, 3840, 1080), Monitor200().VirtualToPhysical(new Rect(0, 540, 1920, 540)));
        }

        [TestMethod]
        public void VirtualToPhysical_OneVirtualPixelShortOfFullScreen_IsNotSnapped()
        {
            Assert.AreEqual(new Rect(0, 0, 3838, 2160), Monitor200().VirtualToPhysical(new Rect(0, 0, 1919, 1080)));
            Assert.AreEqual(new Rect(2, 0, 3838, 2160), Monitor200().VirtualToPhysical(new Rect(1, 0, 1919, 1080)));
        }

        [TestMethod]
        public void VirtualToPhysical_RegionReachingPastTheEdge_IsClippedToThePhysicalSurface()
        {
            Assert.AreEqual(new Rect(3600, 2000, 240, 160), Monitor200().VirtualToPhysical(new Rect(1800, 1000, 500, 500)));
        }

        [TestMethod]
        public void VirtualToPhysical_RegionOutsideTheMonitor_IsEmpty()
        {
            Assert.AreEqual(Rect.Empty, Monitor200().VirtualToPhysical(new Rect(5000, 5000, 100, 100)));
        }

        [TestMethod]
        public void VirtualToPhysical_FractionalScale_RoundsToWholePhysicalPixels()
        {
            // 2560 / 1707 = 1.49971; 1440 / 960 = 1.5.
            Rect mapped = Monitor150().VirtualToPhysical(new Rect(333, 112, 777, 334));

            Assert.AreEqual(new Rect(499, 168, 1165, 501), mapped);
        }

        private const string FullScreenShortcut =
            "suspected bug: MonitorInfo.VirtualToPhysical treats any region with X <= 0, Y <= 0, Width >= monitor width and " +
            "Height >= monitor height as 'the whole monitor' without looking at where it ends. On a scaled monitor a region " +
            "that only partly overlaps it (or not at all) maps to the full physical surface: 1920x1080 seen / 3840x2160 real, " +
            "region (-2000,0,1920,1080) gives (0,0,3840,2160) instead of Empty; (-960,0,1920,1080) gives (0,0,3840,2160) instead " +
            "of (0,0,1920,2160). At 100 % the method is a no-op, so the results differ between scaled and unscaled monitors. " +
            "(ScreenEncoder / CaptureLayout intersect with the monitor first, so recordings are not affected today.)";

        [TestMethod]
        public void VirtualToPhysical_OversizedRegionEntirelyBesideTheMonitor_IsEmpty()
        {
            // As large as the monitor, but lying completely to its left / above it: nothing of
            // it is on the monitor, so nothing may be captured.
            Assert.AreEqual(Rect.Empty, Monitor200().VirtualToPhysical(new Rect(-2000, 0, 1920, 1080)));
            Assert.AreEqual(Rect.Empty, Monitor200().VirtualToPhysical(new Rect(0, -1200, 1920, 1080)));
        }

        [TestMethod]
        public void VirtualToPhysical_OversizedRegionPartlyOnTheMonitor_IsOnlyTheVisiblePart()
        {
            // Monitor-sized region shifted half a screen to the left: only its right half is on
            // the monitor (virtual 0..960 → physical 0..1920).
            Assert.AreEqual(new Rect(0, 0, 1920, 2160), Monitor200().VirtualToPhysical(new Rect(-960, 0, 1920, 1080)));
        }

        [TestMethod]
        public void VirtualToPhysical_AnisotropicScale_ScalesEachAxisByItsOwnRatio()
        {
            var monitor = new MonitorInfo { Right = 1920, Bottom = 1200, PhysicalWidth = 3840, PhysicalHeight = 1800 };

            Assert.AreEqual(new Rect(200, 150, 800, 600), monitor.VirtualToPhysical(new Rect(100, 100, 400, 400)));
        }

        [TestMethod]
        public void VirtualToPhysical_MonitorWithoutSize_ReturnsTheRegionUnchanged()
        {
            var unknown = new MonitorInfo { PhysicalWidth = 3840, PhysicalHeight = 2160 };
            var region = new Rect(10, 20, 300, 200);

            Assert.AreEqual(region, unknown.VirtualToPhysical(region));
        }

        [TestMethod]
        public void VirtualToPhysical_UsesTheMonitorSize_NotItsDesktopPosition()
        {
            // The region is monitor-relative: the same region maps identically wherever the
            // monitor sits on the desktop.
            Rect onSecondary = SecondaryRight200().VirtualToPhysical(new Rect(100, 50, 640, 360));

            Assert.AreEqual(new Rect(200, 100, 1280, 720), onSecondary);
            Assert.AreEqual(new Rect(0, 0, 2560, 1440), SecondaryRight200().VirtualToPhysical(new Rect(0, 0, 1280, 720)));
        }

        [TestMethod]
        public void VirtualToPhysical_ResultAlwaysLiesWithinThePhysicalSurface()
        {
            var random = new System.Random(58);
            foreach (MonitorInfo monitor in new[] { Monitor200(), Monitor150(), Monitor125(), SecondaryLeft150() })
            {
                var surface = new Rect(0, 0, monitor.PhysicalWidth, monitor.PhysicalHeight);
                for (int i = 0; i < 2000; i++)
                {
                    double x = random.Next(0, monitor.Width);
                    double y = random.Next(0, monitor.Height);
                    double w = random.Next(1, monitor.Width - (int)x + 1);
                    double h = random.Next(1, monitor.Height - (int)y + 1);

                    Rect mapped = monitor.VirtualToPhysical(new Rect(x, y, w, h));

                    Assert.IsFalse(mapped.IsEmpty, $"({x},{y},{w},{h}) mapped to nothing");
                    Assert.IsTrue(surface.Contains(mapped), $"({x},{y},{w},{h}) mapped to {mapped}, outside {surface}");
                    Assert.AreEqual(System.Math.Round(mapped.X), mapped.X, "whole pixels");
                    Assert.AreEqual(System.Math.Round(mapped.Width), mapped.Width, "whole pixels");

                    // Within a pixel of the exact scaled size.
                    double scaleX = (double)monitor.PhysicalWidth / monitor.Width;
                    double scaleY = (double)monitor.PhysicalHeight / monitor.Height;
                    Assert.IsTrue(System.Math.Abs(mapped.Width - w * scaleX) <= 1.5 + 1e-9, $"width {mapped.Width} for {w} virtual pixels");
                    Assert.IsTrue(System.Math.Abs(mapped.Height - h * scaleY) <= 1.5 + 1e-9, $"height {mapped.Height} for {h} virtual pixels");
                }
            }
        }

        #endregion

        #region DesktopPhysicalToVirtual

        [TestMethod]
        public void DesktopPhysicalToVirtual_200PercentSecondary_HalvesRelativeToTheMonitorOrigin()
        {
            // Ported from the harness.
            Assert.AreEqual(new Rect(2780, 100, 400, 300), SecondaryRight200().DesktopPhysicalToVirtual(new Rect(3000, 200, 800, 600)));
        }

        [TestMethod]
        public void DesktopPhysicalToVirtual_100Percent_IsIdentity()
        {
            // Ported from the harness.
            Assert.AreEqual(new Rect(100, 200, 300, 400), Monitor100().DesktopPhysicalToVirtual(new Rect(100, 200, 300, 400)));
        }

        [TestMethod]
        public void DesktopPhysicalToVirtual_EmptyRect_StaysEmpty()
        {
            // Ported from the harness.
            Assert.AreEqual(Rect.Empty, SecondaryRight200().DesktopPhysicalToVirtual(Rect.Empty));
        }

        [TestMethod]
        public void DesktopPhysicalToVirtual_WholeMonitor_MapsToItsVirtualBounds()
        {
            foreach (MonitorInfo monitor in new[] { SecondaryRight200(), SecondaryLeft150(), Monitor200(), Monitor150(), Monitor125() })
                Assert.AreEqual(monitor.Bounds, monitor.DesktopPhysicalToVirtual(monitor.PhysicalBounds));
        }

        [TestMethod]
        public void DesktopPhysicalToVirtual_MonitorLeftOfThePrimary_HandlesNegativeCoordinates()
        {
            // scale 1707/2560 = 0.66680; physical (-1000,300) is 1560 px right of the monitor's left edge.
            Rect mapped = SecondaryLeft150().DesktopPhysicalToVirtual(new Rect(-1000, 300, 600, 300));

            Assert.AreEqual(new Rect(-667, 200, 400, 200), mapped);
        }

        [TestMethod]
        public void DesktopPhysicalToVirtual_SameScaleButShiftedOrigin_Translates()
        {
            // A 100% monitor right of a 200% primary: its physical origin (3840) differs from
            // the origin a DPI-unaware process sees (1920) although its size is unchanged.
            var monitor = new MonitorInfo
            {
                Left = 1920, Top = 0, Right = 3840, Bottom = 1080,
                PhysicalLeft = 3840, PhysicalTop = 0, PhysicalWidth = 1920, PhysicalHeight = 1080,
            };

            Assert.AreEqual(new Rect(2080, 100, 300, 200), monitor.DesktopPhysicalToVirtual(new Rect(4000, 100, 300, 200)));
        }

        [TestMethod]
        public void DesktopPhysicalToVirtual_MonitorWithoutPhysicalSize_ReturnsTheRectUnchanged()
        {
            var unknown = new MonitorInfo { Right = 1920, Bottom = 1080 };
            var rect = new Rect(10, 20, 300, 200);

            Assert.AreEqual(rect, unknown.DesktopPhysicalToVirtual(rect));
        }

        [TestMethod]
        public void DesktopPhysicalToVirtual_WindowSpanningPastTheMonitor_IsNotClipped()
        {
            // A window may hang over the monitor edge; the mapping is a pure scale.
            Rect mapped = SecondaryRight200().DesktopPhysicalToVirtual(new Rect(4920, 1240, 800, 600));

            Assert.AreEqual(new Rect(3740, 620, 400, 300), mapped);
        }

        [TestMethod]
        public void DesktopPhysicalToVirtual_ThenVirtualToPhysical_RoundTripsOnAnExactScale()
        {
            MonitorInfo monitor = SecondaryRight200();
            var physicalWindow = new Rect(2760, 200, 1280, 720); // desktop-absolute, on the monitor

            Rect virtualDesktop = monitor.DesktopPhysicalToVirtual(physicalWindow);
            var monitorRelative = new Rect(virtualDesktop.X - monitor.Left, virtualDesktop.Y - monitor.Top, virtualDesktop.Width, virtualDesktop.Height);
            Rect physicalRelative = monitor.VirtualToPhysical(monitorRelative);

            Assert.AreEqual(new Rect(2660, 100, 640, 360), virtualDesktop);
            Assert.AreEqual(
                new Rect(physicalWindow.X - monitor.PhysicalLeft, physicalWindow.Y - monitor.PhysicalTop, physicalWindow.Width, physicalWindow.Height),
                physicalRelative);
        }

        #endregion

        #region Real monitors

        [TestMethod]
        [TestCategory("Hardware")]
        public void GetActiveMonitorInfos_ReportsConsistentMonitors()
        {
            MonitorInfo[] monitors = MonitorInfo.GetActiveMonitorInfos();

            Assert.IsNotNull(monitors, "the shell concatenates this list without a null check");
            if (monitors.Length == 0)
                Assert.Inconclusive("No display attached to this session.");

            Assert.AreEqual(1, monitors.Count(m => m.IsPrimary), "exactly one primary monitor");
            Assert.AreEqual(monitors.Length, monitors.Select(m => m.DeviceName).Distinct().Count(), "device names are unique");

            for (int i = 0; i < monitors.Length; i++)
            {
                MonitorInfo m = monitors[i];
                Assert.IsFalse(string.IsNullOrEmpty(m.DeviceName));
                Assert.AreEqual(i, m.OutputIndex);
                Assert.IsTrue(m.Width > 0 && m.Height > 0, $"{m.DeviceName}: virtual size {m.Width}x{m.Height}");
                Assert.IsTrue(m.PhysicalWidth > 0 && m.PhysicalHeight > 0, $"{m.DeviceName}: physical size {m.PhysicalWidth}x{m.PhysicalHeight}");
                Assert.IsTrue(m.RefreshRate >= 0);

                // The lookups used by the shell find the same monitor again.
                Assert.AreEqual(m.DeviceName, MonitorInfo.GetMonitorInfo(m.DeviceName)?.DeviceName);

                // A full-screen region maps to the whole capture surface.
                Assert.AreEqual(
                    new Rect(0, 0, m.PhysicalWidth, m.PhysicalHeight),
                    m.VirtualToPhysical(new Rect(0, 0, m.Width, m.Height)));
            }

            MonitorInfo primary = MonitorInfo.GetPrimaryMonitorInfo();
            Assert.IsNotNull(primary);
            Assert.IsTrue(primary.IsPrimary);
            Assert.AreEqual(0, primary.Left);
            Assert.AreEqual(0, primary.Top);

            Assert.IsNull(MonitorInfo.GetMonitorInfo(@"\\.\DISPLAY_DOES_NOT_EXIST"));
        }

        #endregion
    }
}
