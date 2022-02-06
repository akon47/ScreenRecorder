using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ScreenRecorder.Region
{
    public class RegionSelectionResult
    {
        public string DeviceName { get; private set; }
        public Rect Region { get; private set; }
        public IntPtr Hwnd { get; private set; }

        public RegionSelectionResult(string deviceName, Rect region, IntPtr hwnd)
        {
            DeviceName = deviceName;
            Region = region;
            Hwnd = hwnd;
        }
    }
}
