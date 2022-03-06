using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace ScreenRecorder.DirectX
{
    public class MonitorInfo : ICaptureTarget
    {
        #region Static Methods
        static public MonitorInfo GetPrimaryMonitorInfo()
        {
            return GetActiveMonitorInfos().FirstOrDefault(x => x.IsPrimary);
        }

        static public MonitorInfo GetMonitorInfo(string deviceName)
        {
            return GetActiveMonitorInfos().FirstOrDefault(x => x.DeviceName.Equals(deviceName));
        }

        static public MonitorInfo[] GetActiveMonitorInfos()
        {
            List<MonitorInfo> monitorInfos = new List<MonitorInfo>();
            using (Factory1 factory = new Factory1())
            {
                int adapterCount = factory.GetAdapterCount1();
                for (int i = 0; i < adapterCount; i++)
                {
                    using (Adapter1 adapter = factory.GetAdapter1(i))
                    {
                        int outputCount = adapter.GetOutputCount();
                        string primaryDeviceName = System.Windows.Forms.Screen.PrimaryScreen.DeviceName;
                        for (int j = 0; j < outputCount; j++)
                        {
                            using (SharpDX.DXGI.Output output = adapter.GetOutput(j))
                            {
                                if (output.Description.IsAttachedToDesktop)
                                {
                                    monitorInfos.Add(new MonitorInfo()
                                    {
                                        AdapterDescription = adapter.Description1.Description,
                                        DeviceName = output.Description.DeviceName,
                                        AdapterIndex = i,
                                        OutputIndex = j,
                                        IsPrimary = (primaryDeviceName.Equals(output.Description.DeviceName)),
                                        Left = output.Description.DesktopBounds.Left,
                                        Top = output.Description.DesktopBounds.Top,
                                        Right = output.Description.DesktopBounds.Right,
                                        Bottom = output.Description.DesktopBounds.Bottom
                                    });
                                }
                            }
                        }
                    }
                }
            }

            return monitorInfos.Count > 0 ? monitorInfos.ToArray() : null;
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
        public int Width
        {
            get
            {
                return Right - Left;
            }
        }

        public int Height
        {
            get
            {
                return Bottom - Top;
            }
        }

        public System.Windows.Rect Bounds
        {
            get
            {
                return new System.Windows.Rect(Left, Top, Width, Height);
            }
        }

        public string Description
        {
            get
            {
                return string.Format("{0}: {1}x{2} @ {3},{4}{5}", AdapterDescription, Width, Height, Left, Top, IsPrimary ? $" ({ScreenRecorder.Properties.Resources.PrimaryDisplay})" : "");
            }
        }
    }
}
