using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ScreenRecorder.Region
{
    public sealed class WindowRegion
    {
        #region Native Methods
        public static Rect GetWindowRectangle(IntPtr hWnd)
        {
            Rect rect;

            int size = Marshal.SizeOf(typeof(Rect));
            DwmGetWindowAttribute(hWnd, (int)DwmWindowAttribute.DwmwaExtendedFrameBounds, out rect, size);

            return rect;
        }

        [Flags]
        private enum DwmWindowAttribute : uint
        {
            DwmwaNcrenderingEnabled = 1,
            DwmwaNcrenderingPolicy,
            DwmwaTransitionsForcedisabled,
            DwmwaAllowNcpaint,
            DwmwaCaptionButtonBounds,
            DwmwaNonclientRtlLayout,
            DwmwaForceIconicRepresentation,
            DwmwaFlip3DPolicy,
            DwmwaExtendedFrameBounds,
            DwmwaHasIconicBitmap,
            DwmwaDisallowPeek,
            DwmwaExcludedFromPeek,
            DwmwaCloak,
            DwmwaCloaked,
            DwmwaFreezeRepresentation,
            DwmwaLast
        }

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out Rect pvAttribute, int cbAttribute);

        [DllImport("user32.DLL")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hwnd, out Rect lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;        // x position of upper-left corner
            public int Top;         // y position of upper-left corner
            public int Right;       // x position of lower-right corner
            public int Bottom;      // y position of lower-right corner
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool EnumWindows(WindowEnumProc callback, int extraData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
        public delegate bool WindowEnumProc(IntPtr hwnd, IntPtr lparam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumChildWindows(IntPtr hwnd, WindowEnumProc callback, IntPtr lParam);
        #endregion

        public System.Windows.Rect Region { get; private set; }
        public IntPtr Hwnd { get; private set; }

        /// <summary>
        /// Get the windows you see on the screen from the front.
        /// </summary>
        /// <returns></returns>
        public static WindowRegion[] GetWindowRegions()
        {
            List<WindowRegion> windowRegions = new List<WindowRegion>();

            EnumWindows((hWnd, lparam) =>
            {
                if(IsWindowVisible(hWnd) && !Utils.IsWindowDisplayedOnlyMonitor(hWnd))
                {
                    Rect rect = GetWindowRectangle(hWnd);

                    System.Windows.Rect region = new System.Windows.Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                    if (region.Height > 16 && region.Width > 16)
                    {
                        windowRegions.Add(new WindowRegion() { Region = region, Hwnd = hWnd });
                    }
                }
                return true;
            }, 0);

            return windowRegions.Count > 0 ? windowRegions.ToArray() : null;
        }
    }
}
