using System;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using ScreenRecorder.DirectX;

namespace ScreenRecorder.VideoSource
{
    /// <summary>
    /// Windows.Graphics.Capture session. FrameArrived (on a free-threaded pool thread) copies the
    /// captured BGRA texture into a device-owned "latest" texture; the paced capture thread later
    /// pulls the crop out of it via <see cref="TryCopyLatest"/>. The recorder's immediate context
    /// is multithread-protected, so the cross-thread copies are safe.
    /// </summary>
    internal sealed class WgcCapture : IDisposable
    {
        private readonly object _lock = new object();
        private readonly Direct3D11Device _device;
        private readonly bool _drawCursor;

        private IDirect3DDevice _winrtDevice;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;
        private GraphicsCaptureItem _item;
        private ID3D11Texture2D _latestBgra;
        private SizeInt32 _size;
        private bool _hasFrame;
        private bool _disposed;

        public event Action Closed;

        public SizeInt32 ItemSize => _size;

        public WgcCapture(Direct3D11Device device, GraphicsCaptureItem item, bool drawCursor)
        {
            _device = device;
            _item = item;
            _drawCursor = drawCursor;
            _size = item.Size;

            _latestBgra = CreateBgraTexture(_size);

            _winrtDevice = WgcInterop.CreateWinRtDevice(device.Device);
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
            _framePool.FrameArrived += OnFrameArrived;
            _session = _framePool.CreateCaptureSession(item);

            if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
                _session.IsCursorCaptureEnabled = drawCursor;
            if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
                _session.IsBorderRequired = false;

            item.Closed += (s, e) => Closed?.Invoke();
        }

        public void Start()
        {
            _session.StartCapture();
        }

        /// <summary>
        /// Copies the crop region of the latest captured frame into <paramref name="dstRegion"/>.
        /// Returns false if no frame has arrived yet.
        /// </summary>
        public bool TryCopyLatest(ID3D11DeviceContext context, ID3D11Texture2D dstRegion, in Box crop)
        {
            lock (_lock)
            {
                if (!_hasFrame || _latestBgra == null)
                    return false;

                context.CopySubresourceRegion(dstRegion, 0, 0, 0, 0, _latestBgra, 0, crop);
                return true;
            }
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null)
                return;

            lock (_lock)
            {
                if (_disposed)
                    return;

                if (frame.ContentSize.Width != _size.Width || frame.ContentSize.Height != _size.Height)
                {
                    _size = frame.ContentSize;
                    _latestBgra?.Dispose();
                    _latestBgra = CreateBgraTexture(_size);
                    _framePool.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
                }

                using var tex = WgcInterop.GetTexture(frame.Surface);
                _device.Context.CopyResource(_latestBgra, tex);
                _hasFrame = true;
            }
        }

        private ID3D11Texture2D CreateBgraTexture(SizeInt32 size)
        {
            var desc = new Texture2DDescription
            {
                Width = (uint)size.Width,
                Height = (uint)size.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None,
            };
            return _device.Device.CreateTexture2D(desc);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }

            _session?.Dispose();
            if (_framePool != null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
                _framePool.Dispose();
            }

            _latestBgra?.Dispose();
            _winrtDevice?.Dispose();
            _item = null;
        }
    }
}
