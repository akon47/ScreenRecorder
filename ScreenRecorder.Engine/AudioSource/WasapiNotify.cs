using System;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace ScreenRecorder.AudioSource
{
    /// <summary>
    /// Re-raises WASAPI device-change COM callbacks (IMMNotificationClient) as .NET events so a
    /// capture can restart on default-device change / hot-plug. Callbacks arrive on an MTA COM
    /// thread — subscribers must be thread-safe.
    /// </summary>
    internal sealed class WasapiNotify : IMMNotificationClient, IDisposable
    {
        public readonly struct DefaultDeviceChange
        {
            public readonly DataFlow Flow;
            public readonly Role Role;
            public readonly string DeviceId;

            public DefaultDeviceChange(DataFlow flow, Role role, string deviceId)
            {
                Flow = flow;
                Role = role;
                DeviceId = deviceId;
            }
        }

        public readonly struct DeviceStateChange
        {
            public readonly string DeviceId;
            public readonly DeviceState NewState;

            public DeviceStateChange(string deviceId, DeviceState newState)
            {
                DeviceId = deviceId;
                NewState = newState;
            }
        }

        private MMDeviceEnumerator _enumerator;
        private bool _disposed;

        public event Action<DefaultDeviceChange> DefaultDeviceChanged;
        public event Action<DeviceStateChange> DeviceStateChanged;

        public WasapiNotify()
        {
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
            => DefaultDeviceChanged?.Invoke(new DefaultDeviceChange(flow, role, defaultDeviceId));

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
            => DeviceStateChanged?.Invoke(new DeviceStateChange(deviceId, newState));

        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                _enumerator?.UnregisterEndpointNotificationCallback(this);
            }
            catch { }
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }
}
