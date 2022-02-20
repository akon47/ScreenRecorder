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
        public string DeviceName { get; }
        public Rect Region { get; }
        public IntPtr Hwnd { get; }

        public RegionSelectionResult(string deviceName, Rect region, IntPtr hwnd)
        {
            DeviceName = deviceName;
            Region = region;
            Hwnd = hwnd;
        }
    }
}
