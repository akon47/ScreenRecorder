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
        public RegionSelectionMode RegionSelectionMode { get; }
        /// <summary>
        /// Selected display device name
        /// </summary>
        public string DeviceName { get; }
        /// <summary>
        /// selected display bounds
        /// </summary>
        public Rect DisplayBounds { get; }
        /// <summary>
        /// selected region
        /// </summary>
        public Rect Region { get; }
        /// <summary>
        /// is operation cancelled
        /// </summary>
        public bool IsCancelled { get; }

        public RegionSelectedEventArgs(RegionSelectionMode regionSelectionMode, string deviceName, Rect displayBounds, Rect region, bool isCancelled)
        {
            RegionSelectionMode = regionSelectionMode;
            DeviceName = deviceName;
            DisplayBounds = displayBounds;
            Region = region;
            IsCancelled = isCancelled;
        }
    }

    public delegate void RegionSelectedHandler(object sender, RegionSelectedEventArgs e);
}
