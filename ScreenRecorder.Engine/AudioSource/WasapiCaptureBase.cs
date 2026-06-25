using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MediaEncoder;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenRecorder.AudioSource
{
    /// <summary>
    /// Shared WASAPI capture mechanics for loopback (render endpoint) and microphone (capture
    /// endpoint). Resolves the device, starts NAudio capture, converts each DataAvailable block to
    /// interleaved float with a QPC timestamp back-dated to the block start (same clock domain as
    /// the video t0), and restarts on default-device change / hot-plug. The float buffer in
    /// <see cref="DataReady"/> is reused across callbacks — consume it synchronously.
    /// </summary>
    internal abstract class WasapiCaptureBase : IDisposable
    {
        private readonly object _lock = new object();
        private readonly DataFlow _dataFlow;
        private readonly bool _useDefaultDevice;
        private readonly string _deviceId;

        private WasapiCapture _capture;
        private WasapiNotify _notify;
        private float[] _floatBuffer = Array.Empty<float>();
        private string _trackedDeviceId;
        private int _sampleRate;
        private int _channels;
        private int _bytesPerSampleValue;
        private bool _disposed;

        /// <summary>(interleaved float [L,R,...], sampleFrames, qpcBlockStartTicks)</summary>
        public event Action<float[], int, long> DataReady;

        public WaveFormat WaveFormat { get; private set; }
        public bool IsActive { get; private set; }

        protected WasapiCaptureBase(DataFlow dataFlow, bool useDefaultDevice, string deviceId)
        {
            _dataFlow = dataFlow;
            _useDefaultDevice = useDefaultDevice;
            _deviceId = deviceId;
        }

        /// <summary>Creates the NAudio capture for a resolved device (loopback vs mic differ here).</summary>
        protected abstract WasapiCapture CreateCapture(MMDevice device);

        public void Start()
        {
            _notify = new WasapiNotify();
            _notify.DefaultDeviceChanged += OnDefaultDeviceChanged;
            _notify.DeviceStateChanged += OnDeviceStateChanged;
            Initialize();
        }

        private void Initialize()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                StopCaptureLocked();

                MMDevice device = ResolveDevice();
                if (device == null)
                {
                    IsActive = false;
                    return; // No device (e.g. no render endpoint / no mic). Source stays silent.
                }

                try
                {
                    if (device.State != DeviceState.Active)
                        return;

                    _trackedDeviceId = device.ID;
                    _capture = CreateCapture(device);
                    WaveFormat = _capture.WaveFormat;
                    _sampleRate = WaveFormat.SampleRate;
                    _channels = WaveFormat.Channels;
                    _bytesPerSampleValue = (WaveFormat.BitsPerSample + 7) / 8;

                    _capture.DataAvailable += OnDataAvailable;
                    _capture.RecordingStopped += OnRecordingStopped;
                    _capture.StartRecording();
                    IsActive = true;
                }
                finally
                {
                    device.Dispose(); // capture holds its own reference
                }
            }
        }

        private MMDevice ResolveDevice()
        {
            using var en = new MMDeviceEnumerator();
            try
            {
                if (_useDefaultDevice)
                {
                    if (!en.HasDefaultAudioEndpoint(_dataFlow, Role.Multimedia))
                        return null;
                    return en.GetDefaultAudioEndpoint(_dataFlow, Role.Multimedia);
                }

                return en.GetDevice(_deviceId);
            }
            catch
            {
                return null;
            }
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0)
                return;

            long ts = Stopwatch.GetTimestamp();
            int frames = e.BytesRecorded / _bytesPerSampleValue / _channels;
            ts -= AudioHelper.GetDurationFromSamples(frames, _sampleRate);

            int floatCount = e.BytesRecorded / 4;
            float[] buffer;
            lock (_lock)
            {
                if (_disposed)
                    return;
                if (_floatBuffer.Length < floatCount)
                    _floatBuffer = new float[floatCount];
                buffer = _floatBuffer;

                // Shared-mode WASAPI delivers IEEE float-32; reinterpret the byte span.
                var src = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
                src.CopyTo(buffer.AsSpan(0, floatCount));
            }

            DataReady?.Invoke(buffer, frames, ts);
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null && !_disposed)
                Initialize(); // recover from a driver error that didn't fire a device notify
        }

        private void OnDefaultDeviceChanged(WasapiNotify.DefaultDeviceChange change)
        {
            if (_useDefaultDevice && change.Flow == _dataFlow && change.Role == Role.Multimedia)
                Initialize();
        }

        private void OnDeviceStateChanged(WasapiNotify.DeviceStateChange change)
        {
            if (change.DeviceId == _trackedDeviceId)
                Initialize();
        }

        private void StopCaptureLocked()
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                try { _capture.StopRecording(); } catch { }
                _capture.Dispose();
                _capture = null;
            }
            IsActive = false;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                _disposed = true;

                if (_notify != null)
                {
                    _notify.DefaultDeviceChanged -= OnDefaultDeviceChanged;
                    _notify.DeviceStateChanged -= OnDeviceStateChanged;
                    _notify.Dispose();
                    _notify = null;
                }

                StopCaptureLocked();
            }
        }
    }
}
