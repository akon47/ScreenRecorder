using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScreenRecorder
{
    public interface ICaptureTarget
    {
        string DeviceName { get; }
        string Description { get; }
    }

    public class CaptureTarget : ICaptureTarget
    {
        public const string PrimaryCaptureTargetDeviceName = "\\\\PRIMARY_DISPLAY_CAPTURE_TARGET\\\\";

        static public readonly CaptureTarget PrimaryDisplay = new CaptureTarget(PrimaryCaptureTargetDeviceName, ScreenRecorder.Properties.Resources.PrimaryDisplay);

        public string DeviceName { get; private set; }
        public string Description { get; private set; }

        public CaptureTarget(string deviceName, string description)
        {
            DeviceName = deviceName;
            Description = description;
        }
    }
}
