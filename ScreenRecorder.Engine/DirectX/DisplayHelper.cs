using System;
using System.Runtime.InteropServices;

namespace ScreenRecorder.DirectX
{
    /// <summary>
    /// Resolves a display device name (e.g. \\.\DISPLAY1, as carried by MonitorInfo.DeviceName /
    /// System.Windows.Forms.Screen.DeviceName) to an HMONITOR for WGC's CreateForMonitor, via
    /// EnumDisplayMonitors + GetMonitorInfo (no Vanara dependency).
    /// </summary>
    internal static class DisplayHelper
    {
        public static IntPtr GetMonitorHandleFromDeviceName(string deviceName)
        {
            IntPtr found = IntPtr.Zero;

            bool Callback(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data)
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(hMonitor, ref info)
                    && string.Equals(info.szDevice, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    found = hMonitor;
                    return false; // stop enumeration
                }

                return true;
            }

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);

            if (found == IntPtr.Zero)
            {
                // Fallback: nearest monitor to the primary origin.
                found = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
            }

            return found;
        }

        private const uint MONITOR_DEFAULTTOPRIMARY = 1;

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }
    }
}
