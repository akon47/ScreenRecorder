using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using MediaEncoder;
using ScreenRecorder.Encoder;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.Capture;
using ScreenRecorder.DirectX;
using Rect = System.Windows.Rect;

namespace ScreenRecorder.VideoSource
{
    /// <summary>
    /// The screen capture source: owns the WGC capture, the NV12 converter, and the paced master
    /// clock, and pushes one CFR NV12 frame per grid tick into the recorder. WGC delivers frames
    /// on its own cadence (latest-texture only); the paced clock samples at the target fps and
    /// re-pushes the held frame on late/duplicate ticks for exact CFR.
    /// </summary>
    public sealed unsafe class ScreenVideoSource : IDisposable
    {
        private readonly Recorder _recorder;
        private readonly int _fpsNum;
        private readonly int _fpsDen;
        private readonly long _t0;

        private readonly Direct3D11Device _device;
        private readonly WgcCapture _capture;
        private readonly Nv12Converter _nv12;
        private readonly ID3D11Texture2D _regionTex;
        private readonly Box _crop;
        private readonly int _outW;
        private readonly int _outH;

        private PacedClock _clock;
        private Thread _thread;
        private CancellationTokenSource _cts;

        private nint _outBuffer;
        private int _outBufferSize;
        private int _lastStride;
        private bool _hasHeld;
        private bool _disposed;

        public ScreenVideoSource(string deviceName, Rect region, bool drawCursor, int fpsNumerator, int fpsDenominator, long t0, Recorder recorder)
            : this(WgcInterop.CreateItemForMonitor(DisplayHelper.GetMonitorHandleFromDeviceName(deviceName)), region, drawCursor, fpsNumerator, fpsDenominator, t0, recorder)
        {
        }

        public ScreenVideoSource(IntPtr hwnd, bool drawCursor, int fpsNumerator, int fpsDenominator, long t0, Recorder recorder)
            : this(WgcInterop.CreateItemForWindow(hwnd), Rect.Empty, drawCursor, fpsNumerator, fpsDenominator, t0, recorder)
        {
        }

        private ScreenVideoSource(GraphicsCaptureItem item, Rect region, bool drawCursor, int fpsNumerator, int fpsDenominator, long t0, Recorder recorder)
        {
            _recorder = recorder;
            _fpsNum = fpsNumerator;
            _fpsDen = fpsDenominator;
            _t0 = t0;

            int itemW = item.Size.Width;
            int itemH = item.Size.Height;

            // region == Empty => full item (window mode / full monitor); else clamp to the item.
            Rect clamped = region == Rect.Empty
                ? new Rect(0, 0, itemW, itemH)
                : Rect.Intersect(region, new Rect(0, 0, itemW, itemH));

            int left = (int)clamped.Left;
            int top = (int)clamped.Top;
            _outW = Math.Max(2, (int)clamped.Width & ~1);
            _outH = Math.Max(2, (int)clamped.Height & ~1);
            _crop = new Box(left, top, 0, left + _outW, top + _outH, 1);

            _device = new Direct3D11Device();
            _capture = new WgcCapture(_device, item, drawCursor);
            _capture.Closed += OnCaptureClosed;

            _regionTex = _device.Device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)_outW,
                Height = (uint)_outH,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None,
            });

            _nv12 = new Nv12Converter(_device.Device, _device.Context);
        }

        public long DroppedFrames => _clock?.DroppedFrames ?? 0;

        public void Pause() => _clock?.Pause();

        public void Resume() => _clock?.Resume();

        public void Start()
        {
            _capture.Start();
            _clock = new PacedClock(_fpsNum, _fpsDen, _t0, OnTick);
            _cts = new CancellationTokenSource();
            _thread = new Thread(() => _clock.Run(_cts.Token))
            {
                Name = "ScreenCaptureClock",
                IsBackground = true,
                Priority = ThreadPriority.Highest,
            };
            _thread.Start();
        }

        private void OnTick(long ptsQpc, long gridIndex, bool isDuplicate)
        {
            if (!isDuplicate && _capture.TryCopyLatest(_device.Context, _regionTex, _crop))
            {
                if (_nv12.Convert(_regionTex, _outW, _outH, out nint plane, out int stride, out int slice))
                {
                    EnsureOutBuffer(slice);
                    Unsafe.CopyBlockUnaligned((void*)_outBuffer, (void*)plane, (uint)slice);
                    _nv12.Unmap();
                    _lastStride = stride;
                    _hasHeld = true;
                }
            }

            if (_hasHeld)
                _recorder.PushVideoFrame(new RawVideoFrame(_outBuffer, _lastStride, _outW, _outH, PixelFormat.NV12, ptsQpc));
        }

        private void EnsureOutBuffer(int size)
        {
            if (size <= _outBufferSize)
                return;

            _outBuffer = _outBuffer == 0 ? Marshal.AllocHGlobal(size) : Marshal.ReAllocHGlobal(_outBuffer, size);
            _outBufferSize = size;
        }

        private void OnCaptureClosed()
        {
            // Capture target went away (display disconnected / window closed).
            _cts?.Cancel();
        }

        public void Stop()
        {
            _cts?.Cancel();
            _thread?.Join(1000);
            _thread = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            Stop();
            _capture?.Dispose();
            _nv12?.Dispose();
            _regionTex?.Dispose();
            _device?.Dispose();

            if (_outBuffer != 0)
            {
                Marshal.FreeHGlobal(_outBuffer);
                _outBuffer = 0;
            }
        }
    }
}
