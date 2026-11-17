using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace ScreenRecorder.AudioSource
{
    /// <summary>An audio endpoint the user can pick as a recording source.</summary>
    public sealed class AudioDeviceInfo
    {
        /// <summary>WASAPI endpoint id; empty = "follow the system default device".</summary>
        public string Id { get; }

        public string Name { get; }

        public AudioDeviceInfo(string id, string name)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
        }

        public override string ToString() => Name;
    }

    /// <summary>Enumerates the active audio endpoints (playback devices for loopback, recording devices).</summary>
    public static class AudioDevices
    {
        /// <summary>Active playback devices — the candidates for system-audio (loopback) capture.</summary>
        public static AudioDeviceInfo[] GetOutputDevices() => Enumerate(DataFlow.Render);

        /// <summary>Active recording devices (microphones, line-in, virtual inputs).</summary>
        public static AudioDeviceInfo[] GetInputDevices() => Enumerate(DataFlow.Capture);

        private static AudioDeviceInfo[] Enumerate(DataFlow flow)
        {
            var result = new List<AudioDeviceInfo>();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                {
                    try
                    {
                        result.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
                    }
                    catch
                    {
                        // A device that disappears mid-enumeration is simply left out.
                    }
                    finally
                    {
                        device.Dispose();
                    }
                }
            }
            catch
            {
                // No audio service / no endpoints: an empty list.
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return result.ToArray();
        }
    }
}
