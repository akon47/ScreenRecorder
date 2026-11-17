using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
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
    ///
    /// Locking: a capture is stopped/disposed (which joins its capture thread) only under
    /// <c>_initGate</c>, and the capture thread itself never takes that gate — the data callback
    /// is lock-free and a capture-thread stop notification re-initializes from the thread pool.
    /// Holding a lock the capture thread also wants while joining it is a classic stop-time
    /// deadlock.
    /// </summary>
    internal abstract class WasapiCaptureBase : IDisposable
    {
        private enum SampleKind
        {
            Float32,
            Pcm16,
            Pcm24,
            Pcm32,
            Unsupported,
        }

        private readonly object _initGate = new object();
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
        private SampleKind _sampleKind;
        private int _formatGeneration;
        private volatile bool _disposed;

        /// <summary>(interleaved float [L,R,...], sampleFrames, qpcBlockStartTicks)</summary>
        public event Action<float[], int, long> DataReady;

        public WaveFormat WaveFormat { get; private set; }
        public bool IsActive { get; private set; }

        /// <summary>Sample rate of the running capture (0 when inactive).</summary>
        public int SampleRate => _sampleRate;

        /// <summary>Channel count of the running capture (0 when inactive).</summary>
        public int Channels => _channels;

        /// <summary>Incremented every time the capture (and possibly its format) was restarted.</summary>
        public int FormatGeneration => Volatile.Read(ref _formatGeneration);

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
            lock (_initGate)
            {
                if (_disposed || _notify != null)
                    return;

                _notify = new WasapiNotify();
                _notify.DefaultDeviceChanged += OnDefaultDeviceChanged;
                _notify.DeviceStateChanged += OnDeviceStateChanged;
            }

            Initialize();
        }

        private void Initialize()
        {
            lock (_initGate)
            {
                if (_disposed)
                    return;

                StopCapture();

                MMDevice device = ResolveDevice();
                if (device == null)
                    return; // No device (e.g. no render endpoint / no mic). Source stays silent.

                try
                {
                    if (device.State != DeviceState.Active)
                        return;

                    var capture = CreateCapture(device);
                    var format = capture.WaveFormat;

                    _trackedDeviceId = device.ID;
                    WaveFormat = format;
                    _sampleRate = format.SampleRate;
                    _channels = format.Channels;
                    _bytesPerSampleValue = (format.BitsPerSample + 7) / 8;
                    _sampleKind = Classify(format);
                    Interlocked.Increment(ref _formatGeneration);

                    capture.DataAvailable += OnDataAvailable;
                    capture.RecordingStopped += OnRecordingStopped;
                    _capture = capture;
                    capture.StartRecording();
                    IsActive = true;
                }
                catch
                {
                    // Device vanished between enumeration and start, exclusive-mode conflict...
                    StopCapture();
                }
                finally
                {
                    device.Dispose(); // capture holds its own reference
                }
            }
        }

        private static SampleKind Classify(WaveFormat format)
        {
            WaveFormat standard = format is WaveFormatExtensible extensible ? extensible.ToStandardWaveFormat() : format;

            if (standard.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
                return SampleKind.Float32;

            if (standard.Encoding == WaveFormatEncoding.Pcm)
            {
                switch (format.BitsPerSample)
                {
                    case 16: return SampleKind.Pcm16;
                    case 24: return SampleKind.Pcm24;
                    case 32: return SampleKind.Pcm32;
                }
            }

            return SampleKind.Unsupported;
        }

        private MMDevice ResolveDevice()
        {
            try
            {
                using var en = new MMDeviceEnumerator();
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
            if (_disposed || e.BytesRecorded <= 0 || _sampleKind == SampleKind.Unsupported)
                return;

            int channels = _channels;
            int bytesPerSample = _bytesPerSampleValue;
            if (channels <= 0 || bytesPerSample <= 0)
                return;

            long ts = Stopwatch.GetTimestamp();
            int frames = e.BytesRecorded / bytesPerSample / channels;
            if (frames <= 0)
                return;

            ts -= AudioHelper.GetDurationFromSamples(frames, _sampleRate);

            int sampleCount = frames * channels;
            if (_floatBuffer.Length < sampleCount)
                _floatBuffer = new float[sampleCount];

            ConvertToFloat(e.Buffer.AsSpan(0, sampleCount * bytesPerSample), _floatBuffer.AsSpan(0, sampleCount), _sampleKind);

            DataReady?.Invoke(_floatBuffer, frames, ts);
        }

        /// <summary>
        /// Shared-mode WASAPI almost always delivers IEEE float-32 (reinterpreted in place),
        /// but an endpoint's mix format may be integer PCM — convert those explicitly.
        /// </summary>
        private static void ConvertToFloat(ReadOnlySpan<byte> src, Span<float> dst, SampleKind kind)
        {
            switch (kind)
            {
                case SampleKind.Float32:
                    MemoryMarshal.Cast<byte, float>(src).CopyTo(dst);
                    break;

                case SampleKind.Pcm16:
                {
                    var s16 = MemoryMarshal.Cast<byte, short>(src);
                    for (int i = 0; i < dst.Length; i++)
                        dst[i] = s16[i] * (1f / 32768f);
                    break;
                }

                case SampleKind.Pcm24:
                    for (int i = 0; i < dst.Length; i++)
                    {
                        int o = i * 3;
                        int v = (src[o] | (src[o + 1] << 8) | ((sbyte)src[o + 2] << 16));
                        dst[i] = v * (1f / 8388608f);
                    }
                    break;

                case SampleKind.Pcm32:
                {
                    var s32 = MemoryMarshal.Cast<byte, int>(src);
                    for (int i = 0; i < dst.Length; i++)
                        dst[i] = s32[i] * (1f / 2147483648f);
                    break;
                }
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            // Raised on the capture thread: recovering inline would join this very thread.
            if (e.Exception != null && !_disposed)
                ThreadPool.UnsafeQueueUserWorkItem(static state => ((WasapiCaptureBase)state).Initialize(), this);
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

        /// <summary>Stops and disposes the current capture. Call under <c>_initGate</c> only.</summary>
        private void StopCapture()
        {
            var capture = _capture;
            _capture = null;
            IsActive = false;

            if (capture == null)
                return;

            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            try { capture.StopRecording(); } catch { }
            try { capture.Dispose(); } catch { }
        }

        public void Dispose()
        {
            _disposed = true;

            WasapiNotify notify;
            lock (_initGate)
            {
                notify = _notify;
                _notify = null;
                StopCapture();
            }

            // Unregistering waits for in-flight device notifications, and a notification may be
            // waiting for the gate — so never unregister while holding it.
            if (notify != null)
            {
                notify.DefaultDeviceChanged -= OnDefaultDeviceChanged;
                notify.DeviceStateChanged -= OnDeviceStateChanged;
                notify.Dispose();
            }
        }
    }
}
