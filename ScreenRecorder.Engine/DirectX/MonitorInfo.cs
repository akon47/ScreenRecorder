using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenRecorder.DirectX
{
    /// <summary>
    /// Monitor enumeration. P0: uses <see cref="Screen"/> (SharpDX removed) so the capture-target
    /// combobox lists real displays. P2 replaces this with DXGI/WGC adapter+output enumeration
    /// carrying the adapter/output indices needed for real capture targeting.
    /// </summary>
    public class MonitorInfo : ICaptureTarget
    {
        #region Static Methods

        public static MonitorInfo GetPrimaryMonitorInfo()
        {
            return GetActiveMonitorInfos().FirstOrDefault(x => x.IsPrimary);
        }

        public static MonitorInfo GetMonitorInfo(string deviceName)
        {
            return GetActiveMonitorInfos().FirstOrDefault(x => x.DeviceName.Equals(deviceName));
        }

        public static MonitorInfo[] GetActiveMonitorInfos()
        {
            var monitorInfos = new List<MonitorInfo>();
            int outputIndex = 0;
            foreach (var screen in Screen.AllScreens)
            {
                GetPhysicalResolution(screen, out int physicalWidth, out int physicalHeight);
                monitorInfos.Add(new MonitorInfo()
                {
                    AdapterDescription = "Display Adapter",
                    DeviceName = screen.DeviceName,
                    AdapterIndex = 0,
                    OutputIndex = outputIndex++,
                    IsPrimary = screen.Primary,
                    Left = screen.Bounds.Left,
                    Top = screen.Bounds.Top,
                    Right = screen.Bounds.Right,
                    Bottom = screen.Bounds.Bottom,
                    PhysicalWidth = physicalWidth,
                    PhysicalHeight = physicalHeight,
                });
            }

            // Never null — AppManager concatenates this list directly.
            return monitorInfos.ToArray();
        }

        /// <summary>
        /// The monitor's real pixel size from the current display mode. Unlike
        /// <see cref="Screen.Bounds"/>, EnumDisplaySettings is NOT subject to DPI virtualization,
        /// so this stays correct even in a DPI-unaware process.
        /// </summary>
        private static void GetPhysicalResolution(Screen screen, out int width, out int height)
        {
            var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(screen.DeviceName, ENUM_CURRENT_SETTINGS, ref mode)
                && mode.dmPelsWidth > 0 && mode.dmPelsHeight > 0)
            {
                width = (int)mode.dmPelsWidth;
                height = (int)mode.dmPelsHeight;
            }
            else
            {
                width = screen.Bounds.Width;
                height = screen.Bounds.Height;
            }
        }

        #endregion

        public string AdapterDescription { get; set; }
        public string DeviceName { get; set; }
        public int AdapterIndex { get; set; }
        public int OutputIndex { get; set; }
        public bool IsPrimary { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
        public int Width => Right - Left;
        public int Height => Bottom - Top;

        /// <summary>
        /// Real pixel resolution of the monitor — the space WGC capture items are sized in.
        /// In a DPI-unaware process <see cref="Width"/>/<see cref="Height"/> are the
        /// DPI-virtualized size (a 3840×2160 monitor at 200% scale reads as 1920×1080) while
        /// this stays 3840×2160; both are equal at 100% scale or in a DPI-aware process.
        /// </summary>
        public int PhysicalWidth { get; set; }
        public int PhysicalHeight { get; set; }

        public System.Windows.Rect Bounds => new System.Windows.Rect(Left, Top, Width, Height);
        public string Description => $"{AdapterDescription}: {PhysicalWidth}x{PhysicalHeight} @ {Left},{Top}{(IsPrimary ? " (Primary)" : "")}";

        /// <summary>
        /// Maps a monitor-relative region from this process's (possibly DPI-virtualized)
        /// coordinate space into the monitor's physical pixel space, the space WGC captures in.
        /// Without this a DPI-unaware process at 200% scale crops recording to the top-left
        /// quarter (GitHub issue #58). No-op when the two spaces already match.
        /// </summary>
        public System.Windows.Rect VirtualToPhysical(System.Windows.Rect region)
        {
            if (region.IsEmpty || Width <= 0 || Height <= 0
                || (PhysicalWidth == Width && PhysicalHeight == Height))
                return region;

            // A region covering the whole virtual monitor maps to the whole physical surface
            // exactly, immune to the rounding Windows applies at fractional scale factors.
            if (region.X <= 0 && region.Y <= 0 && region.Width >= Width && region.Height >= Height)
                return new System.Windows.Rect(0, 0, PhysicalWidth, PhysicalHeight);

            double scaleX = (double)PhysicalWidth / Width;
            double scaleY = (double)PhysicalHeight / Height;
            var scaled = new System.Windows.Rect(
                Math.Round(region.X * scaleX),
                Math.Round(region.Y * scaleY),
                Math.Round(region.Width * scaleX),
                Math.Round(region.Height * scaleY));
            return System.Windows.Rect.Intersect(scaled, new System.Windows.Rect(0, 0, PhysicalWidth, PhysicalHeight));
        }

        #region Interop

        private const int ENUM_CURRENT_SETTINGS = -1;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }

        #endregion
    }
}
