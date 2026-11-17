using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using ScreenRecorder.DirectX;

namespace ScreenRecorder.VideoSource
{
    /// <summary>
    /// Windows.Graphics.Capture session. WGC raises FrameArrived (on a free-threaded pool
    /// thread) only when the captured content actually changed; the handler just swaps the new
    /// frame in as "latest" and bumps <see cref="FrameSerial"/>. The paced capture thread
    /// compares the serial to know whether anything changed since it last looked, and converts
    /// straight out of the held frame's texture — the frame is never copied on arrival.
    ///
    /// Frames must be taken as they arrive: when the pool is full WGC drops the NEWEST frame,
    /// so a consumer that only polled could be left holding stale content forever.
    ///
    /// The per-frame path uses raw COM vtable calls instead of the WinRT projection, so it
    /// creates no runtime-callable wrappers — nothing for the GC to collect per frame.
    /// </summary>
    internal sealed unsafe class WgcCapture : IDisposable
    {
        private const int BufferCount = 2; // one held as "latest" + one free for the next frame

        private static readonly Guid FramePoolIid = new("24EB6D22-1975-422E-82E7-780DBD8DDF24");
        private static readonly Guid DxgiInterfaceAccessIid = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
        private static readonly Guid Texture2DIid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

        private readonly object _lock = new object();
        private readonly Direct3D11Device _device;

        private IDirect3DDevice _winrtDevice;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;
        private GraphicsCaptureItem _item;

        private nint _poolAbi;        // IDirect3D11CaptureFramePool* (owned reference)
        private nint _latestFrame;    // IDirect3D11CaptureFrame* (owned reference)
        private nint _latestTexture;  // ID3D11Texture2D* (owned reference)
        private int _latestWidth;
        private int _latestHeight;
        private SizeInt32 _poolSize;
        private long _serial;
        private int _poolGeneration;
        private int _callbackFaults;
        private bool _disposed;

        /// <summary>Raised when the capture target goes away (window closed / display removed).</summary>
        public event Action Closed;

        /// <summary>Incremented for every new frame; lets the consumer detect "content changed".</summary>
        public long FrameSerial => Volatile.Read(ref _serial);

        /// <summary>Incremented whenever the frame pool (and its textures) was recreated.</summary>
        public int PoolGeneration => Volatile.Read(ref _poolGeneration);

        /// <summary>Size of the capture item when the session was created (physical pixels).</summary>
        public SizeInt32 InitialSize { get; }

        public WgcCapture(Direct3D11Device device, GraphicsCaptureItem item, bool drawCursor, bool showBorder = false)
        {
            _device = device;
            _item = item;
            _poolSize = item.Size;
            InitialSize = item.Size;

            _winrtDevice = WgcInterop.CreateWinRtDevice(device.Device);
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, BufferCount, _poolSize);

            // Raw interface pointer for the allocation-free per-frame path.
            nint inspectable = MarshalInspectable<Direct3D11CaptureFramePool>.FromManaged(_framePool);
            try
            {
                Guid iid = FramePoolIid;
                Marshal.QueryInterface(inspectable, in iid, out _poolAbi);
            }
            finally
            {
                Marshal.Release(inspectable);
            }

            _framePool.FrameArrived += OnFrameArrived;
            _session = _framePool.CreateCaptureSession(item);

            if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
                _session.IsCursorCaptureEnabled = drawCursor;
            if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
                _session.IsBorderRequired = showBorder;

            item.Closed += OnItemClosed;
        }

        public void Start()
        {
            _session.StartCapture();
        }

        /// <summary>
        /// Locks the latest captured frame for conversion. On success the caller owns the lock
        /// until <see cref="UnlockLatest"/> and may read <paramref name="texture"/> (a borrowed
        /// ID3D11Texture2D* of <paramref name="width"/>×<paramref name="height"/> pixels).
        /// Returns false (lock not held) when no frame has arrived yet.
        /// </summary>
        public bool TryLockLatest(out nint texture, out int width, out int height, out long serial)
        {
            Monitor.Enter(_lock);
            if (_disposed || _latestTexture == 0)
            {
                Monitor.Exit(_lock);
                texture = 0;
                width = height = 0;
                serial = 0;
                return false;
            }

            texture = _latestTexture;
            width = _latestWidth;
            height = _latestHeight;
            serial = _serial;
            return true;
        }

        public void UnlockLatest() => Monitor.Exit(_lock);

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            // An exception escaping a WinRT callback tears the process down — contain it.
            try
            {
                if (_poolAbi != 0)
                    TakeFrameRaw();
                else
                    TakeFrameProjected(sender);
            }
            catch
            {
                Interlocked.Increment(ref _callbackFaults);
            }
        }

        private void TakeFrameRaw()
        {
            nint frame = 0;
            var tryGetNextFrame = (delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(void***)_poolAbi)[7];
            if (tryGetNextFrame(_poolAbi, &frame) < 0 || frame == 0)
                return;

            SizeInt32 size = default;
            var getContentSize = (delegate* unmanaged[Stdcall]<nint, SizeInt32*, int>)(*(void***)frame)[8];
            if (getContentSize(frame, &size) < 0)
            {
                Marshal.Release(frame);
                return;
            }

            Accept(frame, GetFrameTexture(frame), size);
        }

        /// <summary>Fallback when the raw frame-pool interface is unavailable (allocates wrappers).</summary>
        private void TakeFrameProjected(Direct3D11CaptureFramePool sender)
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null)
                return;

            SizeInt32 size = frame.ContentSize;

            nint frameAbi = MarshalInspectable<Direct3D11CaptureFrame>.FromManaged(frame); // +1 ref
            using var tex = WgcInterop.GetTexture(frame.Surface);
            nint texture = tex.NativePointer;
            Marshal.AddRef(texture);

            Accept(frameAbi, texture, size);
        }

        /// <summary>Takes ownership of <paramref name="frame"/> and <paramref name="texture"/>.</summary>
        private void Accept(nint frame, nint texture, SizeInt32 size)
        {
            lock (_lock)
            {
                if (_disposed || size.Width <= 0 || size.Height <= 0)
                {
                    Release(ref texture);
                    Release(ref frame);
                    return;
                }

                if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
                {
                    // The target changed size (window resize / display mode change): this frame
                    // still has the old buffer size, so drop it and rebuild the pool.
                    Release(ref texture);
                    Release(ref frame);
                    ReleaseLatestLocked();

                    _poolSize = size;
                    _framePool.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, BufferCount, size);
                    Interlocked.Increment(ref _poolGeneration);
                    return;
                }

                // A frame taken from the previous pool can still arrive right after a Recreate;
                // trust the texture's real dimensions, not the pool size.
                if (texture == 0 || !TryGetTextureSize(texture, out int texW, out int texH)
                    || texW < size.Width || texH < size.Height)
                {
                    Release(ref texture);
                    Release(ref frame);
                    return;
                }

                ReleaseLatestLocked();
                _latestFrame = frame;
                _latestTexture = texture;
                _latestWidth = size.Width;
                _latestHeight = size.Height;
                Interlocked.Increment(ref _serial);
            }
        }

        /// <summary>Raw ID3D11Texture2D::GetDesc (vtable slot 10) — width/height only.</summary>
        private static bool TryGetTextureSize(nint texture, out int width, out int height)
        {
            uint* desc = stackalloc uint[11]; // D3D11_TEXTURE2D_DESC
            var getDesc = (delegate* unmanaged[Stdcall]<nint, uint*, void>)(*(void***)texture)[10];
            getDesc(texture, desc);
            width = (int)desc[0];
            height = (int)desc[1];
            return width > 0 && height > 0;
        }

        private static nint GetFrameTexture(nint frame)
        {
            nint surface = 0;
            var getSurface = (delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(void***)frame)[6];
            if (getSurface(frame, &surface) < 0 || surface == 0)
                return 0;

            try
            {
                Guid accessIid = DxgiInterfaceAccessIid;
                if (Marshal.QueryInterface(surface, in accessIid, out nint access) < 0 || access == 0)
                    return 0;

                try
                {
                    Guid textureIid = Texture2DIid;
                    nint texture = 0;
                    var getInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(void***)access)[3];
                    return getInterface(access, &textureIid, &texture) >= 0 ? texture : 0;
                }
                finally
                {
                    Marshal.Release(access);
                }
            }
            finally
            {
                Marshal.Release(surface);
            }
        }

        private void ReleaseLatestLocked()
        {
            Release(ref _latestTexture);
            Release(ref _latestFrame);
        }

        private static void Release(ref nint pointer)
        {
            if (pointer != 0)
            {
                Marshal.Release(pointer);
                pointer = 0;
            }
        }

        private void OnItemClosed(GraphicsCaptureItem sender, object args)
        {
            try { Closed?.Invoke(); } catch { }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                _disposed = true;
                ReleaseLatestLocked();
            }

            if (_item != null)
            {
                try { _item.Closed -= OnItemClosed; } catch { }
            }

            if (_framePool != null)
            {
                try { _framePool.FrameArrived -= OnFrameArrived; } catch { }
            }

            try { _session?.Dispose(); } catch { }
            try { _framePool?.Dispose(); } catch { }

            Release(ref _poolAbi);

            try { _winrtDevice?.Dispose(); } catch { }
            _item = null;
        }
    }
}
