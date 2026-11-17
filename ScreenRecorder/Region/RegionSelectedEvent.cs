using System;
using System.Windows;

namespace ScreenRecorder.Region
{
    public class RegionSelectedEventArgs : EventArgs
    {
        /// <summary>Mode the selection was made in.</summary>
        public RegionSelectionMode RegionSelectionMode { get; }

        /// <summary>Device name of the display the selection is on.</summary>
        public string DeviceName { get; }

        /// <summary>Bounds of that display (desktop coordinates).</summary>
        public Rect DisplayBounds { get; }

        /// <summary>Selected rectangle (desktop coordinates).</summary>
        public Rect Region { get; }

        /// <summary>True when the user cancelled instead of selecting.</summary>
        public bool IsCancelled { get; }

        /// <summary>The picked window in window mode, otherwise zero.</summary>
        public IntPtr WindowHandle { get; }

        public RegionSelectedEventArgs(RegionSelectionMode regionSelectionMode, string deviceName, Rect displayBounds, Rect region, bool isCancelled, IntPtr windowHandle)
        {
            RegionSelectionMode = regionSelectionMode;
            DeviceName = deviceName;
            DisplayBounds = displayBounds;
            Region = region;
            IsCancelled = isCancelled;
            WindowHandle = windowHandle;
        }
    }

    public delegate void RegionSelectedHandler(object sender, RegionSelectedEventArgs e);
}
