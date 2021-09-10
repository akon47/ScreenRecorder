using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScreenRecorder
{
    public interface IAudioInputDevice
    {
        string DeviceName { get; }
        string Description { get; }
    }

    public class AudioInputDevice : IAudioInputDevice
    {
        public string DeviceName { get; private set; }
        public string Description { get; private set; }

        public AudioInputDevice(string deviceName, string description)
        {
            DeviceName = deviceName;
            Description = description;
        }
    }
}
