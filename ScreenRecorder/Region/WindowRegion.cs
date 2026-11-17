using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ScreenRecorder.DirectX;
using ScreenRecorder.Services;

namespace ScreenRecorder.Region
{
    /// <summary>A top-level window the user can pick as a capture target, with its on-screen bounds.</summary>
    public sealed class WindowRegion
    {
        #region Native Methods

        private const int DwmwaExtendedFrameBounds = 9;
        private const int DwmwaCloaked = 14;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private delegate bool WindowEnumProc(IntPtr hwnd, IntPtr lparam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out NativeRect pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(WindowEnumProc callback, IntPtr extraData);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        #endregion

        /// <summary>Bounds in this process's (possibly DPI-virtualized) desktop coordinates.</summary>
        public System.Windows.Rect Region { get; private set; }

        public IntPtr Hwnd { get; private set; }

        /// <summary>
        /// The windows visible on screen, front to back. Windows of this app, minimized
        /// windows, cloaked windows (hidden virtual-desktop / suspended store apps, which report
        /// "visible" but show nothing) and windows hidden from capture are left out.
        /// </summary>
        public static WindowRegion[] GetWindowRegions()
        {
            var windowRegions = new List<WindowRegion>();
            MonitorInfo[] monitors = MonitorInfo.GetActiveMonitorInfos();
            uint ownProcessId = (uint)Environment.ProcessId;

            EnumWindows((hWnd, lparam) =>
            {
                if (!IsCapturable(hWnd, ownProcessId))
                    return true;

                System.Windows.Rect? region = GetVirtualBounds(hWnd, monitors);
                if (region != null && region.Value.Height > 16 && region.Value.Width > 16)
                    windowRegions.Add(new WindowRegion { Region = region.Value, Hwnd = hWnd });

                return true;
            }, IntPtr.Zero);

            return windowRegions.Count > 0 ? windowRegions.ToArray() : null;
        }

        /// <summary>
        /// Whether a top-level window can be recorded right now: on screen (not minimized,
        /// not cloaked), not one of this app's own windows and not hidden from capture.
        /// </summary>
        internal static bool IsCapturable(IntPtr hWnd, uint ownProcessId)
        {
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                return false;

            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == ownProcessId)
                return false;

            if (DwmGetWindowAttribute(hWnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;

            return !CaptureExclusionService.IsExcluded(hWnd);
        }

        /// <summary>
        /// Current bounds of a window in this process's desktop coordinates; null when the
        /// window no longer exists or is minimized / hidden.
        /// </summary>
        public static System.Windows.Rect? GetVirtualBounds(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd) || !IsWindowVisible(hWnd) || IsIconic(hWnd))
                return null;

            return GetVirtualBounds(hWnd, MonitorInfo.GetActiveMonitorInfos());
        }

        private static System.Windows.Rect? GetVirtualBounds(IntPtr hWnd, MonitorInfo[] monitors)
        {
            if (DwmGetWindowAttribute(hWnd, DwmwaExtendedFrameBounds, out NativeRect rect, Marshal.SizeOf<NativeRect>()) != 0)
                return null;

            var physical = new System.Windows.Rect(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
            if (physical.Width <= 0 || physical.Height <= 0)
                return null;

            // DWM extended frame bounds are always physical pixels (never DPI-virtualized), so
            // on scaled monitors they must be mapped into this process's coordinate space.
            return ToVirtualDesktop(physical, monitors);
        }

        private static System.Windows.Rect ToVirtualDesktop(System.Windows.Rect physicalRect, MonitorInfo[] monitors)
        {
            var center = new System.Windows.Point(physicalRect.X + physicalRect.Width / 2, physicalRect.Y + physicalRect.Height / 2);
            foreach (var monitor in monitors)
            {
                if (monitor.PhysicalBounds.Contains(center))
                    return monitor.DesktopPhysicalToVirtual(physicalRect);
            }

            foreach (var monitor in monitors)
            {
                if (monitor.PhysicalBounds.IntersectsWith(physicalRect))
                    return monitor.DesktopPhysicalToVirtual(physicalRect);
            }

            return physicalRect;
        }
    }
}
