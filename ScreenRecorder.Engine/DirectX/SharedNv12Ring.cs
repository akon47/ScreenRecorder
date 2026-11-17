using System;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ScreenRecorder.DirectX
{
    /// <summary>
    /// A small ring of shared NV12 textures on the capture device — the hand-over point of the
    /// zero-copy hardware-encode path. The capture thread converts a frame straight into a free
    /// slot (GPU only); the encoder, which runs on its OWN D3D11 device created by FFmpeg, opens
    /// the same textures through NT shared handles and copies the slot into its frame pool. The
    /// two devices only ever meet through these keyed-mutex textures, so no encoder/driver
    /// thread touches the capture device (handing FFmpeg the app's own device is a known source
    /// of deadlocks and driver crashes).
    ///
    /// Slot life cycle (lock-free, one producer + one consumer):
    /// Free → Writing (producer) → Queued → Reading (consumer) → Free.
    /// The producer never waits: if no slot is free the frame is dropped.
    /// </summary>
    internal sealed unsafe class SharedNv12Ring : IDisposable
    {
        public const ulong MutexKey = 0;

        private const int StateFree = 0;
        private const int StateWriting = 1;
        private const int StateQueued = 2;
        private const int StateReading = 3;

        private readonly ID3D11Texture2D[] _textures;
        private readonly IDXGIKeyedMutex[] _mutexes;
        private readonly nint[] _handles;
        private readonly int[] _states;
        private int _next;
        private bool _disposed;

        public int Width { get; }
        public int Height { get; }
        public int Count => _textures.Length;

        /// <summary>
        /// DXGI enumeration index of the adapter the capture device lives on (-1 = unknown →
        /// default adapter). Shared textures can only be opened by a device on the same GPU, so
        /// the encoder device must be created on this adapter.
        /// </summary>
        public int AdapterIndex { get; }

        public SharedNv12Ring(ID3D11Device device, int width, int height, int count = 4)
        {
            if (width <= 0 || height <= 0 || (width & 1) != 0 || (height & 1) != 0)
                throw new ArgumentException("NV12 size must be positive and even.");
            if (count < 2)
                throw new ArgumentOutOfRangeException(nameof(count));

            Width = width;
            Height = height;
            AdapterIndex = FindAdapterIndex(device);

            _textures = new ID3D11Texture2D[count];
            _mutexes = new IDXGIKeyedMutex[count];
            _handles = new nint[count];
            _states = new int[count];

            try
            {
                for (int i = 0; i < count; i++)
                {
                    _textures[i] = device.CreateTexture2D(new Texture2DDescription
                    {
                        Width = (uint)width,
                        Height = (uint)height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.NV12,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.RenderTarget,
                        CPUAccessFlags = CpuAccessFlags.None,
                        MiscFlags = ResourceOptionFlags.SharedKeyedMutex | ResourceOptionFlags.SharedNTHandle,
                    });
                    _mutexes[i] = _textures[i].QueryInterface<IDXGIKeyedMutex>();

                    using var resource = _textures[i].QueryInterface<IDXGIResource1>();
                    _handles[i] = resource.CreateSharedHandle(null, Vortice.DXGI.SharedResourceFlags.Read | Vortice.DXGI.SharedResourceFlags.Write, null);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public ID3D11Texture2D GetTexture(int slot) => _textures[slot];

        public nint GetSharedHandle(int slot) => _handles[slot];

        /// <summary>
        /// Reserves a free slot and takes its keyed mutex for writing. Returns the slot index,
        /// or -1 when every slot is still in flight (the encoder is behind → drop the frame).
        /// </summary>
        public int TryBeginWrite()
        {
            if (_disposed)
                return -1;

            int count = _states.Length;
            for (int i = 0; i < count; i++)
            {
                int slot = (_next + i) % count;
                if (Interlocked.CompareExchange(ref _states[slot], StateWriting, StateFree) != StateFree)
                    continue;

                if (AcquireSync(_mutexes[slot], 0))
                {
                    _next = (slot + 1) % count;
                    return slot;
                }

                Volatile.Write(ref _states[slot], StateFree);
            }

            return -1;
        }

        /// <summary>
        /// Ends a write. The caller must have flushed the device context first so the conversion
        /// is submitted before the encoder device can take the mutex.
        /// </summary>
        public void EndWrite(int slot, bool queued)
        {
            _mutexes[slot].ReleaseSync(MutexKey);
            Volatile.Write(ref _states[slot], queued ? StateQueued : StateFree);
        }

        /// <summary>Consumer: marks a queued slot as being read.</summary>
        public void BeginRead(int slot) => Volatile.Write(ref _states[slot], StateReading);

        /// <summary>Consumer: the slot was copied (or abandoned) and may be written again.</summary>
        public void Release(int slot) => Volatile.Write(ref _states[slot], StateFree);

        /// <summary>
        /// Raw IDXGIKeyedMutex::AcquireSync (vtable slot 8). The managed wrapper throws away the
        /// result code, but WAIT_TIMEOUT is a *success* HRESULT, so it must be checked by hand.
        /// </summary>
        internal static bool AcquireSync(IDXGIKeyedMutex mutex, int timeoutMs)
        {
            nint p = mutex.NativePointer;
            void** vtbl = *(void***)p;
            var acquire = (delegate* unmanaged[Stdcall]<nint, ulong, int, int>)vtbl[8];
            return acquire(p, MutexKey, timeoutMs) == 0;
        }

        /// <summary>
        /// Position of the device's adapter in DXGI enumeration order — the same numbering
        /// FFmpeg's D3D11VA device creation uses.
        /// </summary>
        internal static int FindAdapterIndex(ID3D11Device device)
        {
            try
            {
                using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
                using var deviceAdapter = dxgiDevice.GetAdapter();
                var luid = deviceAdapter.Description.Luid;
                using var factory = deviceAdapter.GetParent<IDXGIFactory1>();
                for (uint index = 0; factory.EnumAdapters1(index, out var adapter).Success; index++)
                {
                    using (adapter)
                    {
                        var candidate = adapter.Description1.Luid;
                        if (candidate.LowPart == luid.LowPart && candidate.HighPart == luid.HighPart)
                            return (int)index;
                    }
                }
            }
            catch
            {
                // Unknown: the caller tries the default adapter and falls back to the CPU path
                // if the shared textures cannot be opened there.
            }

            return -1;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            for (int i = 0; i < _textures.Length; i++)
            {
                _mutexes[i]?.Dispose();
                _textures[i]?.Dispose();
                if (_handles[i] != 0)
                {
                    CloseHandle(_handles[i]);
                    _handles[i] = 0;
                }
            }
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(nint handle);
    }
}
