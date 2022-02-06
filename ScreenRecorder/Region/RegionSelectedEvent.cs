using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ScreenRecorder.Region
{
    public class RegionSelectedEventArgs : EventArgs
    {
        /// <summary>
        /// Region Selection Mode
        /// </summary>
        public RegionSelectionMode RegionSelectionMode { get; private set; }
        /// <summary>
        /// Selected display device name
        /// </summary>
        public string DeviceName { get; private set; }
        /// <summary>
        /// selected display bounds
        /// </summary>
        public Rect DisplayBounds { get; private set; }
        /// <summary>
        /// selected region
        /// </summary>
        public Rect Region { get; private set; }
        /// <summary>
        /// is operation cancelled
        /// </summary>
        public bool IsCancelled { get; private set; }

        public IntPtr Hwnd { get; private set; }

        public RegionSelectedEventArgs(RegionSelectionMode regionSelectionMode, string deviceName, Rect displayBounds, Rect region, IntPtr hwnd, bool isCancelled)
        {
            RegionSelectionMode = regionSelectionMode;
            DeviceName = deviceName;
            DisplayBounds = displayBounds;
            Region = region;
            IsCancelled = isCancelled;
            Hwnd = hwnd;
        }
    }

    public delegate void RegionSelectedHandler(object sender, RegionSelectedEventArgs e);
}
