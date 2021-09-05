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
        public RegionSelectionMode RegionSelectionMode { get; private set; }
        public string DeviceName { get; private set; }
        public Rect DisplayBounds { get; private set; }
        public Rect Region { get; private set; }
        public bool IsCancelled { get; private set; }

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
