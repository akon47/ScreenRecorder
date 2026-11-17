using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.DirectX;
using ScreenRecorder.VideoSource;

namespace ScreenRecorder.Tests.VideoSource
{
    /// <summary>
    /// How a rectangle of the desktop maps onto the monitors it touches (multi-monitor and
    /// whole-desktop recording), including monitors with different DPI scaling.
    /// </summary>
    [TestClass]
    public sealed class CaptureLayoutTests
    {
        /// <summary>A monitor at 100 % scaling: virtual and physical bounds are the same.</summary>
        private static MonitorInfo Monitor(string name, int left, int top, int width, int height)
        {
            return new MonitorInfo
            {
                DeviceName = name,
                Left = left, Top = top, Right = left + width, Bottom = top + height,
                PhysicalLeft = left, PhysicalTop = top, PhysicalWidth = width, PhysicalHeight = height,
            };
        }

        /// <summary>A monitor whose physical size differs from what this process sees (DPI scaling).</summary>
        private static MonitorInfo ScaledMonitor(string name, Rect virtualBounds, Rect physicalBounds)
        {
            return new MonitorInfo
            {
                DeviceName = name,
                Left = (int)virtualBounds.Left, Top = (int)virtualBounds.Top, Right = (int)virtualBounds.Right, Bottom = (int)virtualBounds.Bottom,
                PhysicalLeft = (int)physicalBounds.Left, PhysicalTop = (int)physicalBounds.Top,
                PhysicalWidth = (int)physicalBounds.Width, PhysicalHeight = (int)physicalBounds.Height,
            };
        }

        private static readonly MonitorInfo Primary = Monitor("A", 0, 0, 1920, 1080);
        private static readonly MonitorInfo Right = Monitor("B", 1920, 0, 1920, 1080);

        private static void AssertPart(CaptureLayoutPart part, MonitorInfo monitor, Rect source, Rect destination)
        {
            Assert.AreSame(monitor, part.Monitor, "monitor");
            Assert.AreEqual(source, part.Source, $"source on {monitor.DeviceName}");
            Assert.AreEqual(destination, part.Destination, $"destination of {monitor.DeviceName}");
        }

        /// <summary>The invariants every layout must satisfy, whatever the monitors look like.</summary>
        private static void AssertWellFormed(CaptureLayout layout)
        {
            Assert.IsTrue(layout.Width >= 2 && layout.Height >= 2);
            Assert.AreEqual(0, layout.Width % 2, "4:2:0 video needs an even width");
            Assert.AreEqual(0, layout.Height % 2, "4:2:0 video needs an even height");
            Assert.IsTrue(layout.Parts.Count >= 1);

            var picture = new Rect(0, 0, layout.Width, layout.Height);
            for (int i = 0; i < layout.Parts.Count; i++)
            {
                CaptureLayoutPart part = layout.Parts[i];
                Rect s = part.Source, d = part.Destination;

                foreach (double value in new[] { s.X, s.Y, s.Width, s.Height, d.X, d.Y, d.Width, d.Height })
                    Assert.AreEqual(0.0, value % 2, $"part {i}: coordinate {value} is not an even whole number");

                Assert.IsTrue(s.Width >= 2 && s.Height >= 2, $"part {i}: empty source");
                Assert.IsTrue(new Rect(0, 0, part.Monitor.PhysicalWidth, part.Monitor.PhysicalHeight).Contains(s), $"part {i}: source {s} outside the monitor");
                Assert.IsTrue(picture.Contains(d), $"part {i}: destination {d} outside the {layout.Width}x{layout.Height} picture");

                for (int j = 0; j < i; j++)
                {
                    Rect overlap = Rect.Intersect(d, layout.Parts[j].Destination);
                    Assert.IsTrue(overlap.IsEmpty || overlap.Width == 0 || overlap.Height == 0, $"parts {j} and {i} overlap in the picture");
                }
            }
        }

        #region SnapEven

        [TestMethod]
        public void SnapEven_RoundsPositionAndSizeDownToEven()
        {
            Assert.AreEqual(new Rect(2, 4, 6, 8), CaptureLayout.SnapEven(new Rect(3, 5, 7, 9)));
            Assert.AreEqual(new Rect(100, 50, 640, 360), CaptureLayout.SnapEven(new Rect(100, 50, 640, 360)));
            Assert.AreEqual(new Rect(100, 50, 640, 360), CaptureLayout.SnapEven(new Rect(101, 51, 641, 361)));
            Assert.AreEqual(new Rect(0, 0, 0, 0), CaptureLayout.SnapEven(new Rect(1, 1, 1, 1)));
        }

        [TestMethod]
        public void SnapEven_NegativePosition_RoundsTowardsMinusInfinity()
        {
            Assert.AreEqual(new Rect(-4, -6, 10, 10), CaptureLayout.SnapEven(new Rect(-3, -5, 10, 10)));
            Assert.AreEqual(new Rect(-2, -2, 4, 4), CaptureLayout.SnapEven(new Rect(-1, -2, 5, 4)));
        }

        [TestMethod]
        public void SnapEven_Empty_StaysEmpty()
        {
            Assert.AreEqual(Rect.Empty, CaptureLayout.SnapEven(Rect.Empty));
        }

        #endregion

        #region One monitor

        [TestMethod]
        public void ForMonitorRegion_WholeMonitor_IsTheMonitorSize()
        {
            CaptureLayout layout = CaptureLayout.ForMonitorRegion(Primary, new Rect(0, 0, double.MaxValue, double.MaxValue));

            Assert.AreEqual(1920, layout.Width);
            Assert.AreEqual(1080, layout.Height);
            AssertPart(layout.Parts.Single(), Primary, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1080));
            AssertWellFormed(layout);
        }

        [TestMethod]
        public void ForMonitorRegion_SubRegion_IsCutOutAndPlacedAtTheOrigin()
        {
            CaptureLayout layout = CaptureLayout.ForMonitorRegion(Primary, new Rect(100, 50, 640, 360));

            Assert.AreEqual(640, layout.Width);
            Assert.AreEqual(360, layout.Height);
            AssertPart(layout.Parts.Single(), Primary, new Rect(100, 50, 640, 360), new Rect(0, 0, 640, 360));
        }

        [TestMethod]
        public void ForMonitorRegion_OddRegion_IsSnappedToEven()
        {
            CaptureLayout layout = CaptureLayout.ForMonitorRegion(Primary, new Rect(101, 51, 641, 361));

            Assert.AreEqual(640, layout.Width);
            Assert.AreEqual(360, layout.Height);
            AssertPart(layout.Parts.Single(), Primary, new Rect(100, 50, 640, 360), new Rect(0, 0, 640, 360));
            AssertWellFormed(layout);
        }

        [TestMethod]
        public void ForMonitorRegion_RegionReachingPastTheEdge_IsClipped()
        {
            CaptureLayout layout = CaptureLayout.ForMonitorRegion(Primary, new Rect(1800, 1000, 500, 500));

            Assert.AreEqual(120, layout.Width);
            Assert.AreEqual(80, layout.Height);
            AssertPart(layout.Parts.Single(), Primary, new Rect(1800, 1000, 120, 80), new Rect(0, 0, 120, 80));
        }

        [TestMethod]
        public void ForMonitorRegion_UsesMonitorRelativeCoordinates_WhereverTheMonitorIs()
        {
            CaptureLayout layout = CaptureLayout.ForMonitorRegion(Right, new Rect(100, 50, 640, 360));

            AssertPart(layout.Parts.Single(), Right, new Rect(100, 50, 640, 360), new Rect(0, 0, 640, 360));
        }

        [TestMethod]
        public void ForMonitorRegion_ScaledMonitor_IsInPhysicalPixels()
        {
            // 3840x2160 at 200 %: this process sees 1920x1080.
            MonitorInfo hiDpi = ScaledMonitor("H", new Rect(0, 0, 1920, 1080), new Rect(0, 0, 3840, 2160));

            CaptureLayout full = CaptureLayout.ForMonitorRegion(hiDpi, new Rect(0, 0, 1920, 1080));
            Assert.AreEqual(3840, full.Width);
            Assert.AreEqual(2160, full.Height);
            AssertPart(full.Parts.Single(), hiDpi, new Rect(0, 0, 3840, 2160), new Rect(0, 0, 3840, 2160));

            CaptureLayout part = CaptureLayout.ForMonitorRegion(hiDpi, new Rect(100, 50, 640, 360));
            Assert.AreEqual(1280, part.Width);
            Assert.AreEqual(720, part.Height);
            AssertPart(part.Parts.Single(), hiDpi, new Rect(200, 100, 1280, 720), new Rect(0, 0, 1280, 720));
        }

        [TestMethod]
        public void ForMonitorRegion_FractionalScale_FullScreenIsTheExactPhysicalSize()
        {
            // 2560x1440 at 150 %: seen as 1707x960 (a rounded size).
            MonitorInfo monitor = ScaledMonitor("F", new Rect(0, 0, 1707, 960), new Rect(0, 0, 2560, 1440));

            CaptureLayout layout = CaptureLayout.ForMonitorRegion(monitor, new Rect(0, 0, 1707, 960));

            Assert.AreEqual(2560, layout.Width);
            Assert.AreEqual(1440, layout.Height);
            AssertWellFormed(layout);
        }

        [TestMethod]
        public void ForMonitorRegion_NothingToRecord_IsNull()
        {
            Assert.IsNull(CaptureLayout.ForMonitorRegion(null, new Rect(0, 0, 100, 100)), "no monitor");
            Assert.IsNull(CaptureLayout.ForMonitorRegion(Primary, new Rect(5000, 5000, 100, 100)), "outside the monitor");
            Assert.IsNull(CaptureLayout.ForMonitorRegion(Primary, Rect.Empty), "empty region");
            Assert.IsNull(CaptureLayout.ForMonitorRegion(Primary, new Rect(10, 10, 1, 1)), "smaller than one 2x2 block");
            Assert.IsNull(CaptureLayout.ForMonitorRegion(Primary, new Rect(10, 10, 300, 1.5)), "less than 2 pixels high");
        }

        [TestMethod]
        public void ForMonitorRegion_SmallestRecordableRegion_IsTwoByTwo()
        {
            CaptureLayout layout = CaptureLayout.ForMonitorRegion(Primary, new Rect(10, 10, 2, 2));

            Assert.AreEqual(2, layout.Width);
            Assert.AreEqual(2, layout.Height);
        }

        #endregion

        #region Several monitors

        [TestMethod]
        public void ForDesktopRegion_InsideOneMonitor_EqualsTheMonitorRegion()
        {
            CaptureLayout layout = CaptureLayout.ForDesktopRegion(new[] { Primary, Right }, new Rect(2020, 50, 640, 360));

            Assert.AreEqual(640, layout.Width);
            Assert.AreEqual(360, layout.Height);
            AssertPart(layout.Parts.Single(), Right, new Rect(100, 50, 640, 360), new Rect(0, 0, 640, 360));
            AssertWellFormed(layout);
        }

        [TestMethod]
        public void ForDesktopRegion_SpanningTwoMonitors_JoinsThemSeamlessly()
        {
            CaptureLayout layout = CaptureLayout.ForDesktopRegion(new[] { Primary, Right }, new Rect(1000, 100, 2000, 500));

            Assert.AreEqual(2000, layout.Width);
            Assert.AreEqual(500, layout.Height);
            Assert.AreEqual(2, layout.Parts.Count);
            AssertPart(layout.Parts[0], Primary, new Rect(1000, 100, 920, 500), new Rect(0, 0, 920, 500));
            AssertPart(layout.Parts[1], Right, new Rect(0, 100, 1080, 500), new Rect(920, 0, 1080, 500));
            AssertWellFormed(layout);
        }

        [TestMethod]
        public void ForDesktopRegion_WholeDesktop_MonitorsOfDifferentHeights_LeavesTheGapBlack()
        {
            MonitorInfo small = Monitor("S", 1920, 0, 1280, 1024);

            CaptureLayout layout = CaptureLayout.ForDesktopRegion(new[] { Primary, small }, new Rect(0, 0, 3200, 1080));

            Assert.AreEqual(3200, layout.Width);
            Assert.AreEqual(1080, layout.Height, "the picture is as tall as the tallest monitor");
            AssertPart(layout.Parts[0], Primary, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1080));
            AssertPart(layout.Parts[1], small, new Rect(0, 0, 1280, 1024), new Rect(1920, 0, 1280, 1024));
            AssertWellFormed(layout);
        }

        [TestMethod]
        public void ForDesktopRegion_MonitorLeftOfAndAboveThePrimary_UsesNegativeCoordinates()
        {
            MonitorInfo left = Monitor("L", -1280, -200, 1280, 1024);

            CaptureLayout layout = CaptureLayout.ForDesktopRegion(new[] { Primary, left }, new Rect(-1280, -200, 3200, 1280));

            // Union: x -1280..1920, y -200..1080.
            Assert.AreEqual(3200, layout.Width);
            Assert.AreEqual(1280, layout.Height);
            AssertPart(layout.Parts[0], Primary, new Rect(0, 0, 1920, 1080), new Rect(1280, 200, 1920, 1080));
            AssertPart(layout.Parts[1], left, new Rect(0, 0, 1280, 1024), new Rect(0, 0, 1280, 1024));
            AssertWellFormed(layout);
        }

        [TestMethod]
        public void ForDesktopRegion_StackedMonitors_JoinVertically()
        {
            MonitorInfo below = Monitor("D", 0, 1080, 1920, 1080);

            CaptureLayout layout = CaptureLayout.ForDesktopRegion(new[] { Primary, below }, new Rect(200, 800, 400, 600));

            Assert.AreEqual(400, layout.Width);
            Assert.AreEqual(600, layout.Height);
            AssertPart(layout.Parts[0], Primary, new Rect(200, 800, 400, 280), new Rect(0, 0, 400, 280));
            AssertPart(layout.Parts[1], below, new Rect(200, 0, 400, 320), new Rect(0, 280, 400, 320));
        }

        [TestMethod]
        public void ForDesktopRegion_MixedDpi_EachMonitorContributesItsPhysicalPixels()
        {
            // A 200 % monitor right of a 100 % primary. This process sees it as 1280x720 at
            // x = 1920; physically it is 2560x1440 at x = 1920.
            MonitorInfo hiDpi = ScaledMonitor("H", new Rect(1920, 0, 1280, 720), new Rect(1920, 0, 2560, 1440));

            CaptureLayout layout = CaptureLayout.ForDesktopRegion(new[] { Primary, hiDpi }, new Rect(0, 0, 3200, 1080));

            Assert.AreEqual(1920 + 2560, layout.Width);
            Assert.AreEqual(1440, layout.Height);
            AssertPart(layout.Parts[0], Primary, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1080));
            AssertPart(layout.Parts[1], hiDpi, new Rect(0, 0, 2560, 1440), new Rect(1920, 0, 2560, 1440));
            AssertWellFormed(layout);
        }

        [TestMethod]
        public void ForDesktopRegion_MixedDpi_RegionAcrossTheSeam()
        {
            MonitorInfo hiDpi = ScaledMonitor("H", new Rect(1920, 0, 1280, 720), new Rect(1920, 0, 2560, 1440));

            // 200 virtual pixels on each side of the seam, 100 high, starting at y = 100.
            CaptureLayout layout = CaptureLayout.ForDesktopRegion(new[] { Primary, hiDpi }, new Rect(1720, 100, 400, 100));

            // Left: 200x100 physical at (1720,100). Right: 400x200 physical at (1920,200).
            Assert.AreEqual(600, layout.Width);
            Assert.AreEqual(300, layout.Height);
            AssertPart(layout.Parts[0], Primary, new Rect(1720, 100, 200, 100), new Rect(0, 0, 200, 100));
            AssertPart(layout.Parts[1], hiDpi, new Rect(0, 200, 400, 200), new Rect(200, 100, 400, 200));
            AssertWellFormed(layout);
        }

        [TestMethod]
        public void ForDesktopRegion_MonitorsNotTouched_AreLeftOut()
        {
            MonitorInfo third = Monitor("C", 3840, 0, 1920, 1080);

            CaptureLayout layout = CaptureLayout.ForDesktopRegion(new[] { Primary, Right, third }, new Rect(1900, 0, 100, 100));

            CollectionAssert.AreEqual(new[] { "A", "B" }, layout.Parts.Select(p => p.Monitor.DeviceName).ToArray());
            Assert.AreEqual(100, layout.Width);
        }

        [TestMethod]
        public void ForDesktopRegion_SliverOnASecondMonitor_IsLeftOut()
        {
            // One virtual pixel of the second monitor is not worth a second capture.
            CaptureLayout layout = CaptureLayout.ForDesktopRegion(new[] { Primary, Right }, new Rect(1000, 0, 921, 500));

            Assert.AreSame(Primary, layout.Parts.Single().Monitor);
            Assert.AreEqual(920, layout.Width);
            Assert.AreEqual(500, layout.Height);
        }

        [TestMethod]
        public void ForDesktopRegion_NothingToRecord_IsNull()
        {
            var monitors = new[] { Primary, Right };

            Assert.IsNull(CaptureLayout.ForDesktopRegion(null, new Rect(0, 0, 100, 100)), "no monitor list");
            Assert.IsNull(CaptureLayout.ForDesktopRegion(new MonitorInfo[0], new Rect(0, 0, 100, 100)), "no monitors");
            Assert.IsNull(CaptureLayout.ForDesktopRegion(monitors, Rect.Empty), "empty region");
            Assert.IsNull(CaptureLayout.ForDesktopRegion(monitors, new Rect(-5000, -5000, 100, 100)), "outside every monitor");
            Assert.IsNull(CaptureLayout.ForDesktopRegion(monitors, new Rect(100, 100, 1, 1)), "smaller than one 2x2 block");
        }

        [TestMethod]
        public void ForDesktopRegion_RandomRegionsOnRandomDesktops_AreAlwaysWellFormed()
        {
            var random = new Random(2026);
            for (int round = 0; round < 500; round++)
            {
                // Two or three monitors in a row, each at 100 %, 125 %, 150 % or 200 %.
                var monitors = new List<MonitorInfo>();
                int virtualX = random.Next(-2000, 100);
                int physicalX = virtualX * 2;
                int count = random.Next(2, 4);
                for (int m = 0; m < count; m++)
                {
                    int physicalWidth = new[] { 1920, 2560, 3840, 1366 }[random.Next(4)];
                    int physicalHeight = new[] { 1080, 1440, 2160, 768 }[random.Next(4)];
                    double scale = new[] { 1.0, 1.25, 1.5, 2.0 }[random.Next(4)];
                    int virtualWidth = (int)Math.Round(physicalWidth / scale);
                    int virtualHeight = (int)Math.Round(physicalHeight / scale);
                    int top = random.Next(-300, 300);

                    monitors.Add(ScaledMonitor("M" + m,
                        new Rect(virtualX, top, virtualWidth, virtualHeight),
                        new Rect(physicalX, top * 2, physicalWidth, physicalHeight)));
                    virtualX += virtualWidth;
                    physicalX += physicalWidth;
                }

                Rect desktop = monitors.Select(m => m.Bounds).Aggregate(Rect.Union);
                var region = new Rect(
                    desktop.X + random.Next(0, (int)desktop.Width - 4),
                    desktop.Y + random.Next(0, (int)desktop.Height - 4),
                    random.Next(4, (int)desktop.Width),
                    random.Next(4, (int)desktop.Height));

                CaptureLayout layout = CaptureLayout.ForDesktopRegion(monitors, region);
                if (layout == null)
                    continue; // the region fell into a gap between monitors of different heights

                try
                {
                    AssertWellFormed(layout);
                    foreach (CaptureLayoutPart part in layout.Parts)
                        Assert.AreEqual(part.Source.Size, part.Destination.Size, "unscaled layouts copy pixels 1:1");
                }
                catch (AssertFailedException ex)
                {
                    throw new AssertFailedException($"round {round}, region {region}, monitors {string.Join(" | ", monitors.Select(m => $"{m.Bounds} phys {m.PhysicalBounds}"))}: {ex.Message}", ex);
                }
            }
        }

        #endregion

        #region Scaled (GIF output size)

        [TestMethod]
        public void Scaled_ByOneOrAnInvalidFactor_ReturnsTheSameLayout()
        {
            CaptureLayout layout = CaptureLayout.ForMonitorRegion(Primary, new Rect(0, 0, 1920, 1080));

            Assert.AreSame(layout, layout.Scaled(1.0));
            Assert.AreSame(layout, layout.Scaled(1.0000001));
            Assert.AreSame(layout, layout.Scaled(0));
            Assert.AreSame(layout, layout.Scaled(-0.5));
            Assert.AreSame(layout, layout.Scaled(double.NaN));
        }

        [TestMethod]
        [DataRow(0.5, 960, 540)]
        [DataRow(0.25, 480, 270)]
        [DataRow(0.75, 1440, 810)]
        [DataRow(1.0 / 3, 640, 360)]
        [DataRow(0.1, 192, 108)]
        [DataRow(2.0, 3840, 2160)]
        public void Scaled_SingleMonitor_ScalesThePictureButNotTheSource(double scale, int expectedWidth, int expectedHeight)
        {
            CaptureLayout layout = CaptureLayout.ForMonitorRegion(Primary, new Rect(0, 0, 1920, 1080)).Scaled(scale);

            Assert.AreEqual(expectedWidth, layout.Width);
            Assert.AreEqual(expectedHeight, layout.Height);
            AssertPart(layout.Parts.Single(), Primary, new Rect(0, 0, 1920, 1080), new Rect(0, 0, expectedWidth, expectedHeight));
        }

        [TestMethod]
        public void Scaled_OddResult_IsRoundedDownToEven()
        {
            // 1366x768 * 0.5 = 683x384 → 682x384.
            MonitorInfo laptop = Monitor("N", 0, 0, 1366, 768);

            CaptureLayout layout = CaptureLayout.ForMonitorRegion(laptop, new Rect(0, 0, 1366, 768)).Scaled(0.5);

            Assert.AreEqual(682, layout.Width);
            Assert.AreEqual(384, layout.Height);
        }

        [TestMethod]
        public void Scaled_TinyFactor_NeverGoesBelowTwoPixels()
        {
            CaptureLayout layout = CaptureLayout.ForMonitorRegion(Primary, new Rect(0, 0, 1920, 1080)).Scaled(0.0001);

            Assert.AreEqual(2, layout.Width);
            Assert.AreEqual(2, layout.Height);
            Assert.AreEqual(new Rect(0, 0, 2, 2), layout.Parts.Single().Destination);
        }

        [TestMethod]
        public void Scaled_TwoMonitors_KeepsThemAdjacent()
        {
            CaptureLayout layout = CaptureLayout.ForDesktopRegion(new[] { Primary, Right }, new Rect(0, 0, 3840, 1080)).Scaled(0.5);

            Assert.AreEqual(1920, layout.Width);
            Assert.AreEqual(540, layout.Height);
            AssertPart(layout.Parts[0], Primary, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 960, 540));
            AssertPart(layout.Parts[1], Right, new Rect(0, 0, 1920, 1080), new Rect(960, 0, 960, 540));
        }

        [TestMethod]
        public void Scaled_AnyFactor_PartsStayInsideThePictureOnEvenCoordinates()
        {
            MonitorInfo small = Monitor("S", 1920, 0, 1280, 1024);
            CaptureLayout original = CaptureLayout.ForDesktopRegion(new[] { Primary, small }, new Rect(100, 37, 2900, 951));

            foreach (double scale in new[] { 0.05, 0.1, 0.2, 0.25, 0.3, 1.0 / 3, 0.4, 0.5, 0.6, 2.0 / 3, 0.75, 0.9, 1.5 })
            {
                CaptureLayout layout = original.Scaled(scale);

                Assert.AreEqual(0, layout.Width % 2, $"scale {scale}");
                Assert.AreEqual(0, layout.Height % 2, $"scale {scale}");
                Assert.AreEqual(original.Width * scale, layout.Width, 2.0, $"scale {scale}: width");
                Assert.AreEqual(original.Height * scale, layout.Height, 2.0, $"scale {scale}: height");
                Assert.AreEqual(original.Parts.Count, layout.Parts.Count);

                var picture = new Rect(0, 0, layout.Width, layout.Height);
                for (int i = 0; i < layout.Parts.Count; i++)
                {
                    Rect d = layout.Parts[i].Destination;
                    Assert.AreEqual(original.Parts[i].Source, layout.Parts[i].Source, "sources are never scaled");
                    Assert.IsTrue(picture.Contains(d), $"scale {scale}: part {i} at {d} leaves the {layout.Width}x{layout.Height} picture");
                    foreach (double value in new[] { d.X, d.Y, d.Width, d.Height })
                        Assert.AreEqual(0.0, value % 2, $"scale {scale}: part {i} coordinate {value}");
                    Assert.IsTrue(d.Width >= 2 && d.Height >= 2);

                    // Scaled position and size stay within a few pixels of the ideal (even snapping).
                    Rect ideal = original.Parts[i].Destination;
                    Assert.AreEqual(ideal.X * scale, d.X, 4.0, $"scale {scale}: part {i} x");
                    Assert.AreEqual(ideal.Width * scale, d.Width, 6.0, $"scale {scale}: part {i} width");
                }
            }
        }

        #endregion
    }
}
