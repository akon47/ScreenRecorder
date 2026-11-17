using System;
using FFmpeg.AutoGen;
using ScreenRecorder.DirectX;
using Vortice.DXGI;
using D3D11 = Vortice.Direct3D11;

namespace MediaEncoder
{
    /// <summary>
    /// Encoder-side entrance of the zero-copy hardware-encode path. FFmpeg creates its own
    /// D3D11 device (on the capture device's adapter) plus a frame pool of single NV12
    /// render-target textures; the capture device's <see cref="SharedNv12Ring"/> slots are
    /// opened here through their NT shared handles. Encoding a slot = take its keyed mutex,
    /// copy it into a pool texture, release — so the capture side gets the slot back without
    /// waiting for the encode, and no encoder/driver thread ever touches the capture device.
    /// Used from the video-encode thread only.
    /// </summary>
    internal sealed unsafe class SharedTextureInput : IDisposable
    {
        /// <summary>D3D11_BIND_RENDER_TARGET — required on pool textures the encoder registers.</summary>
        private const uint PoolBindFlags = 0x20;

        /// <summary>Longest wait for a slot's mutex; the capture side holds it for well under a tick.</summary>
        private const int LockTimeoutMilliseconds = 200;

        private const int PoolTextureCacheSize = 16;

        private readonly SharedNv12Ring _ring;
        private readonly D3D11.ID3D11Device _device;
        private readonly D3D11.ID3D11DeviceContext _context;
        private readonly nint[] _poolKeys = new nint[PoolTextureCacheSize];
        private readonly D3D11.ID3D11Texture2D[] _poolTextures = new D3D11.ID3D11Texture2D[PoolTextureCacheSize];
        private int _poolCount;
        private AVBufferRef* _deviceRef;
        private AVBufferRef* _framesRef;
        private D3D11.ID3D11Texture2D[] _sharedTextures = Array.Empty<D3D11.ID3D11Texture2D>();
        private IDXGIKeyedMutex[] _sharedMutexes = Array.Empty<IDXGIKeyedMutex>();
        private bool _disposed;

        /// <summary>The frame pool for the encoder context's <c>hw_frames_ctx</c> (add a reference).</summary>
        public AVBufferRef* FramesContext => _framesRef;

        public SharedTextureInput(SharedNv12Ring ring)
        {
            FFmpegBootstrap.EnsureInitialized();
            _ring = ring ?? throw new ArgumentNullException(nameof(ring));

            AVBufferRef* deviceRef = null;
            string adapter = ring.AdapterIndex >= 0 ? ring.AdapterIndex.ToString() : null;
            int ret = ffmpeg.av_hwdevice_ctx_create(&deviceRef, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, adapter, null, 0);
            if (ret < 0)
                throw new InvalidOperationException($"encoder device creation failed: {FFmpegHelper.GetErrorString(ret)}");

            _deviceRef = deviceRef;
            var deviceContext = (AVD3D11VADeviceContext*)((AVHWDeviceContext*)deviceRef->data)->hwctx;

            // Hold our own references next to FFmpeg's.
            _device = new D3D11.ID3D11Device((nint)deviceContext->device);
            _device.AddRef();
            _context = new D3D11.ID3D11DeviceContext((nint)deviceContext->device_context);
            _context.AddRef();

            try
            {
                _framesRef = ffmpeg.av_hwframe_ctx_alloc(deviceRef);
                if (_framesRef == null)
                    throw new InvalidOperationException("encoder frame pool allocation failed");

                // initial_pool_size 0 = single textures created on demand. A pre-sized pool is a
                // texture ARRAY, and drivers reject NV12 arrays with a render-target binding.
                var frames = (AVHWFramesContext*)_framesRef->data;
                frames->format = AVPixelFormat.AV_PIX_FMT_D3D11;
                frames->sw_format = AVPixelFormat.AV_PIX_FMT_NV12;
                frames->width = ring.Width;
                frames->height = ring.Height;
                frames->initial_pool_size = 0;
                ((AVD3D11VAFramesContext*)frames->hwctx)->BindFlags = PoolBindFlags;

                ret = ffmpeg.av_hwframe_ctx_init(_framesRef);
                if (ret < 0)
                    throw new InvalidOperationException($"encoder frame pool initialization failed: {FFmpegHelper.GetErrorString(ret)}");

                OpenSharedTextures();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void OpenSharedTextures()
        {
            using var device1 = _device.QueryInterfaceOrNull<D3D11.ID3D11Device1>()
                ?? throw new InvalidOperationException("encoder device does not support shared NT handles");

            int count = _ring.Count;
            _sharedTextures = new D3D11.ID3D11Texture2D[count];
            _sharedMutexes = new IDXGIKeyedMutex[count];
            for (int i = 0; i < count; i++)
            {
                // Fails when the two devices are on different GPUs → caller falls back to the CPU path.
                _sharedTextures[i] = device1.OpenSharedResource1<D3D11.ID3D11Texture2D>(_ring.GetSharedHandle(i));
                _sharedMutexes[i] = _sharedTextures[i].QueryInterface<IDXGIKeyedMutex>();
            }
        }

        /// <summary>
        /// Copies ring slot <paramref name="slot"/> into a pool texture and fills
        /// <paramref name="frame"/> (which must be blank) with it. Returns false — leaving the
        /// frame blank — when the slot's mutex could not be taken in time.
        /// </summary>
        public bool TryFillFrame(int slot, AVFrame* frame)
        {
            int ret = ffmpeg.av_hwframe_get_buffer(_framesRef, frame, 0);
            if (ret < 0)
                throw new InvalidOperationException($"encoder frame allocation failed: {FFmpegHelper.GetErrorString(ret)}");

            var mutex = _sharedMutexes[slot];
            if (!SharedNv12Ring.AcquireSync(mutex, LockTimeoutMilliseconds))
            {
                ffmpeg.av_frame_unref(frame);
                return false;
            }

            try
            {
                // data[0] = pool texture, data[1] = subresource index. FFmpeg owns the texture.
                var poolTexture = GetPoolTexture((nint)frame->data[0]);
                _context.CopySubresourceRegion(poolTexture, (uint)(nint)frame->data[1], 0, 0, 0, _sharedTextures[slot], 0);

                // Submit before releasing so the copy is ordered ahead of the slot's next write.
                _context.Flush();
            }
            finally
            {
                mutex.ReleaseSync(SharedNv12Ring.MutexKey);
            }

            return true;
        }

        private D3D11.ID3D11Texture2D GetPoolTexture(nint pointer)
        {
            // The pool recycles a handful of textures: wrap each pointer once (no per-frame allocation).
            for (int i = 0; i < _poolCount; i++)
            {
                if (_poolKeys[i] == pointer)
                    return _poolTextures[i];
            }

            if (_poolCount == PoolTextureCacheSize)
                _poolCount = 0; // borrowed wrappers: nothing to release

            var texture = new D3D11.ID3D11Texture2D(pointer);
            _poolKeys[_poolCount] = pointer;
            _poolTextures[_poolCount] = texture;
            _poolCount++;
            return texture;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            for (int i = 0; i < _sharedTextures.Length; i++)
            {
                _sharedMutexes[i]?.Dispose();
                _sharedTextures[i]?.Dispose();
            }
            _sharedTextures = Array.Empty<D3D11.ID3D11Texture2D>();
            _sharedMutexes = Array.Empty<IDXGIKeyedMutex>();

            // Pool-texture wrappers are borrowed pointers (owned by the frame pool): never released.
            _poolCount = 0;

            if (_framesRef != null)
            {
                var framesRef = _framesRef;
                ffmpeg.av_buffer_unref(&framesRef);
                _framesRef = null;
            }

            _context?.Dispose();
            _device?.Dispose();

            if (_deviceRef != null)
            {
                var deviceRef = _deviceRef;
                ffmpeg.av_buffer_unref(&deviceRef);
                _deviceRef = null;
            }
        }
    }
}
