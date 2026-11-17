using System;
using System.Windows;

namespace ScreenRecorder.Region
{
    public class RegionSelectionResult
    {
        /// <summary>Monitor the selection is on.</summary>
        public string DeviceName { get; private set; }

        /// <summary>Selected rectangle, relative to that monitor (DPI-virtualized units).</summary>
        public Rect Region { get; private set; }

        /// <summary>The same rectangle in desktop coordinates (for the recording-area border).</summary>
        public Rect DesktopRegion { get; private set; }

        /// <summary>
        /// The picked window when the selection was made in window mode; zero for a dragged
        /// area or a whole display. A window is recorded as itself (it stays in the picture
        /// when moved or covered), not as the patch of screen it happened to occupy.
        /// </summary>
        public IntPtr WindowHandle { get; private set; }

        public RegionSelectionMode Mode { get; private set; }

        public RegionSelectionResult(string deviceName, Rect region, Rect desktopRegion, IntPtr windowHandle, RegionSelectionMode mode)
        {
            DeviceName = deviceName;
            Region = region;
            DesktopRegion = desktopRegion;
            WindowHandle = windowHandle;
            Mode = mode;
        }
    }
}
