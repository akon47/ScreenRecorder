using System;
using System.Runtime.InteropServices;

namespace MediaEncoder
{
    /// <summary>
    /// Recycles unmanaged buffers so handing a raw frame or an encoded packet across a thread
    /// boundary costs no allocation in steady state. Rent returns a buffer of at least minSize
    /// bytes (the smallest free one that fits); Return makes it reusable. Sizes are rounded up
    /// to a power of two so buffers of slightly different sizes — encoded packets vary from
    /// frame to frame — still get reused. Buffers left idle for many rents are freed, so a
    /// burst (one huge keyframe) does not pin memory for the rest of the recording.
    /// Bookkeeping is a small struct array scanned linearly: no dictionary, no enumerator, no
    /// garbage.
    /// </summary>
    internal sealed class UnmanagedBufferPool : IDisposable
    {
        private const int MinimumSize = 4096;
        private const int TrimInterval = 256;

        private struct Entry
        {
            public nint Ptr;
            public int Size;
            public long LastUsed;
            public bool InUse;
        }

        private readonly object _lock = new object();
        private readonly int _retentionRents;
        private Entry[] _entries = new Entry[16];
        private int _count;
        private long _rents;
        private bool _disposed;

        /// <param name="retentionRents">A free buffer untouched for this many rents is released.</param>
        public UnmanagedBufferPool(int retentionRents = 600)
        {
            _retentionRents = Math.Max(1, retentionRents);
        }

        /// <summary>Buffers currently allocated (in use + free).</summary>
        public int BufferCount
        {
            get { lock (_lock) return _count; }
        }

        public nint Rent(int minSize)
        {
            int size = RoundUp(minSize);

            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(UnmanagedBufferPool));

                _rents++;
                if (_rents % TrimInterval == 0)
                    TrimLocked();

                int best = -1;
                for (int i = 0; i < _count; i++)
                {
                    ref Entry e = ref _entries[i];
                    if (!e.InUse && e.Size >= size && (best < 0 || e.Size < _entries[best].Size))
                        best = i;
                }

                if (best >= 0)
                {
                    _entries[best].InUse = true;
                    _entries[best].LastUsed = _rents;
                    return _entries[best].Ptr;
                }

                if (_count == _entries.Length)
                    Array.Resize(ref _entries, _entries.Length * 2);

                nint ptr = Marshal.AllocHGlobal(size);
                _entries[_count++] = new Entry { Ptr = ptr, Size = size, LastUsed = _rents, InUse = true };
                return ptr;
            }
        }

        public void Return(nint ptr)
        {
            if (ptr == 0)
                return;

            lock (_lock)
            {
                if (_disposed)
                    return; // Dispose already freed everything

                for (int i = 0; i < _count; i++)
                {
                    if (_entries[i].Ptr == ptr)
                    {
                        _entries[i].InUse = false;
                        _entries[i].LastUsed = _rents;
                        return;
                    }
                }
            }
        }

        /// <summary>Frees buffers that have been idle for longer than the retention window.</summary>
        public void CleanupOldBuffers()
        {
            lock (_lock)
                TrimLocked();
        }

        private void TrimLocked()
        {
            long threshold = _rents - _retentionRents;
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_entries[i].InUse || _entries[i].LastUsed >= threshold)
                    continue;

                Marshal.FreeHGlobal(_entries[i].Ptr);
                _entries[i] = _entries[_count - 1];
                _entries[_count - 1] = default;
                _count--;
            }
        }

        internal static int RoundUp(int minSize)
        {
            if (minSize <= MinimumSize)
                return MinimumSize;
            if (minSize > (1 << 30))
                return minSize;

            // Large buffers (raw frames) are all the same size anyway: round to 1 MiB so a
            // 5.5 MB frame does not balloon to 8 MB.
            const int LargeStep = 1 << 20;
            if (minSize > LargeStep)
                return (minSize + LargeStep - 1) / LargeStep * LargeStep;

            return (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)minSize);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                _disposed = true;

                for (int i = 0; i < _count; i++)
                    Marshal.FreeHGlobal(_entries[i].Ptr);

                _count = 0;
            }
        }
    }
}
