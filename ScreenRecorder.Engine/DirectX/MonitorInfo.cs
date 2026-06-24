using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace ScreenRecorder.DirectX
{
    /// <summary>
    /// Monitor enumeration. P0: uses <see cref="Screen"/> (SharpDX removed) so the capture-target
    /// combobox lists real displays. P2 replaces this with DXGI/WGC adapter+output enumeration
    /// carrying the adapter/output indices needed for real capture targeting.
    /// </summary>
    public class MonitorInfo : ICaptureTarget
    {
        #region Static Methods

        public static MonitorInfo GetPrimaryMonitorInfo()
        {
            return GetActiveMonitorInfos().FirstOrDefault(x => x.IsPrimary);
        }

        public static MonitorInfo GetMonitorInfo(string deviceName)
        {
            return GetActiveMonitorInfos().FirstOrDefault(x => x.DeviceName.Equals(deviceName));
        }

        public static MonitorInfo[] GetActiveMonitorInfos()
        {
            var monitorInfos = new List<MonitorInfo>();
            int outputIndex = 0;
            foreach (var screen in Screen.AllScreens)
            {
                monitorInfos.Add(new MonitorInfo()
                {
                    AdapterDescription = "Display Adapter",
                    DeviceName = screen.DeviceName,
                    AdapterIndex = 0,
                    OutputIndex = outputIndex++,
                    IsPrimary = screen.Primary,
                    Left = screen.Bounds.Left,
                    Top = screen.Bounds.Top,
                    Right = screen.Bounds.Right,
                    Bottom = screen.Bounds.Bottom,
                });
            }

            // Never null — AppManager concatenates this list directly.
            return monitorInfos.ToArray();
        }

        #endregion

        public string AdapterDescription { get; set; }
        public string DeviceName { get; set; }
        public int AdapterIndex { get; set; }
        public int OutputIndex { get; set; }
        public bool IsPrimary { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public System.Windows.Rect Bounds => new System.Windows.Rect(Left, Top, Width, Height);
        public string Description => $"{AdapterDescription}: {Width}x{Height} @ {Left},{Top}{(IsPrimary ? " (Primary)" : "")}";
    }
}
