using System;
using System.Collections.Generic;
using System.Windows;
using ScreenRecorder.DirectX;

namespace ScreenRecorder.VideoSource
{
    /// <summary>One monitor's contribution to a recorded picture.</summary>
    public sealed class CaptureLayoutPart
    {
        public MonitorInfo Monitor { get; }

        /// <summary>What to take from the monitor: monitor-relative, physical pixels.</summary>
        public Rect Source { get; }

        /// <summary>Where it goes in the output picture, in output pixels.</summary>
        public Rect Destination { get; }

        public CaptureLayoutPart(MonitorInfo monitor, Rect source, Rect destination)
        {
            Monitor = monitor;
            Source = source;
            Destination = destination;
        }
    }

    /// <summary>
    /// Works out how a rectangle of the desktop maps onto the monitors it touches. Screen
    /// capture works per monitor, so an area that spans two monitors (or the whole desktop) is
    /// recorded as several captures composed into one picture; this is the geometry for that.
    ///
    /// Input rectangles are in this process's (possibly DPI-virtualized) desktop coordinates —
    /// what WPF and <see cref="System.Windows.Forms.Screen"/> report. Each monitor's share is
    /// converted to that monitor's physical pixels and placed by its position on the physical
    /// desktop, so side-by-side monitors join seamlessly; gaps (monitors of different heights)
    /// stay black. All coordinates are snapped to even numbers (4:2:0 video).
    /// Pure geometry — unit tested.
    /// </summary>
    public sealed class CaptureLayout
    {
        /// <summary>Smallest share of a monitor worth recording (physical pixels per side).</summary>
        private const int MinimumPartSize = 2;

        /// <summary>Output picture size in pixels (even).</summary>
        public int Width { get; }

        public int Height { get; }

        public IReadOnlyList<CaptureLayoutPart> Parts { get; }

        private CaptureLayout(int width, int height, IReadOnlyList<CaptureLayoutPart> parts)
        {
            Width = width;
            Height = height;
            Parts = parts;
        }

        /// <summary>
        /// The layout for <paramref name="desktopRegion"/>, or null when it does not cover a
        /// recordable part of any monitor.
        /// </summary>
        public static CaptureLayout ForDesktopRegion(IReadOnlyList<MonitorInfo> monitors, Rect desktopRegion)
        {
            if (monitors == null || desktopRegion.IsEmpty)
                return null;

            // Each monitor's share, as a rectangle on the physical desktop.
            var shares = new List<(MonitorInfo Monitor, Rect Source, Rect Physical)>();
            foreach (MonitorInfo monitor in monitors)
            {
                Rect part = Rect.Intersect(desktopRegion, monitor.Bounds);
                if (part.IsEmpty || part.Width < 1 || part.Height < 1)
                    continue;

                part.Offset(-monitor.Left, -monitor.Top);
                Rect source = SnapEven(monitor.VirtualToPhysical(part));
                if (source.Width < MinimumPartSize || source.Height < MinimumPartSize)
                    continue;

                var physical = new Rect(monitor.PhysicalLeft + source.X, monitor.PhysicalTop + source.Y, source.Width, source.Height);
                shares.Add((monitor, source, physical));
            }

            if (shares.Count == 0)
                return null;

            Rect union = shares[0].Physical;
            for (int i = 1; i < shares.Count; i++)
                union.Union(shares[i].Physical);

            // The origin is snapped down to even so every part lands on an even offset.
            double originX = Math.Floor(union.X / 2) * 2;
            double originY = Math.Floor(union.Y / 2) * 2;
            int width = Math.Max(MinimumPartSize, ((int)Math.Ceiling(union.Right - originX) + 1) & ~1);
            int height = Math.Max(MinimumPartSize, ((int)Math.Ceiling(union.Bottom - originY) + 1) & ~1);

            var parts = new List<CaptureLayoutPart>(shares.Count);
            foreach (var share in shares)
            {
                var destination = new Rect(share.Physical.X - originX, share.Physical.Y - originY, share.Physical.Width, share.Physical.Height);
                parts.Add(new CaptureLayoutPart(share.Monitor, share.Source, destination));
            }

            return new CaptureLayout(width, height, parts);
        }

        /// <summary>
        /// The layout for a region given relative to one monitor (the classic single-monitor
        /// recording). Null when the region misses the monitor.
        /// </summary>
        public static CaptureLayout ForMonitorRegion(MonitorInfo monitor, Rect monitorRelativeRegion)
        {
            if (monitor == null)
                return null;

            Rect valid = Rect.Intersect(monitorRelativeRegion, new Rect(0, 0, monitor.Width, monitor.Height));
            if (valid.IsEmpty)
                return null;

            Rect source = SnapEven(monitor.VirtualToPhysical(valid));
            if (source.Width < MinimumPartSize || source.Height < MinimumPartSize)
                return null;

            var parts = new[] { new CaptureLayoutPart(monitor, source, new Rect(0, 0, source.Width, source.Height)) };
            return new CaptureLayout((int)source.Width, (int)source.Height, parts);
        }

        /// <summary>
        /// The same layout scaled by <paramref name="scale"/> (GIF output size): the pictures's
        /// size and every destination rectangle shrink; sources are unchanged.
        /// </summary>
        public CaptureLayout Scaled(double scale)
        {
            if (scale <= 0 || double.IsNaN(scale) || Math.Abs(scale - 1.0) < 1e-6)
                return this;

            int width = Math.Max(MinimumPartSize, (int)Math.Round(Width * scale) & ~1);
            int height = Math.Max(MinimumPartSize, (int)Math.Round(Height * scale) & ~1);
            double sx = (double)width / Width;
            double sy = (double)height / Height;

            var parts = new List<CaptureLayoutPart>(Parts.Count);
            foreach (CaptureLayoutPart part in Parts)
            {
                Rect d = part.Destination;
                double left = Math.Floor(d.Left * sx / 2) * 2;
                double top = Math.Floor(d.Top * sy / 2) * 2;
                double right = Math.Min(width, Math.Ceiling(d.Right * sx / 2) * 2);
                double bottom = Math.Min(height, Math.Ceiling(d.Bottom * sy / 2) * 2);
                parts.Add(new CaptureLayoutPart(part.Monitor, part.Source, new Rect(left, top, Math.Max(2, right - left), Math.Max(2, bottom - top))));
            }

            return new CaptureLayout(width, height, parts);
        }

        /// <summary>Position rounded down, size rounded down, all to even numbers.</summary>
        internal static Rect SnapEven(Rect rect)
        {
            if (rect.IsEmpty)
                return rect;

            double x = Math.Floor(rect.X / 2) * 2;
            double y = Math.Floor(rect.Y / 2) * 2;
            double width = (int)rect.Width & ~1;
            double height = (int)rect.Height & ~1;
            return new Rect(x, y, width, height);
        }
    }
}
