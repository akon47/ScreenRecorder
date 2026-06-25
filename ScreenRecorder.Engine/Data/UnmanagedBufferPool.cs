using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MediaEncoder
{
    /// <summary>
    /// Recycles unmanaged buffers so the mandatory copy of an encoded packet (out of the
    /// codec-owned, reused packet buffer) into a thread-handover buffer is allocation-free in
    /// steady state. Rent returns a buffer at least minSize bytes; Return makes it reusable.
    /// Buffers idle for several generations are freed by <see cref="CleanupOldBuffers"/>.
    /// </summary>
    internal sealed class UnmanagedBufferPool : IDisposable
    {
        private sealed class Entry
        {
            public nint Ptr;
            public int Size;
            public long LastUsedGeneration;
        }

        private readonly object _lock = new object();
        private readonly SortedList<int, Queue<Entry>> _free = new SortedList<int, Queue<Entry>>();
        private readonly Dictionary<nint, Entry> _all = new Dictionary<nint, Entry>();
        private readonly int _retentionGenerations;
        private long _generation;
        private bool _disposed;

        public UnmanagedBufferPool(int retentionGenerations = 10)
        {
            _retentionGenerations = retentionGenerations;
        }

        public nint Rent(int minSize)
        {
            if (minSize <= 0)
                minSize = 1;

            lock (_lock)
            {
                _generation++;

                // Smallest free buffer that fits.
                foreach (var kv in _free)
                {
                    if (kv.Key >= minSize && kv.Value.Count > 0)
                    {
                        var entry = kv.Value.Dequeue();
                        entry.LastUsedGeneration = _generation;
                        return entry.Ptr;
                    }
                }

                var ptr = Marshal.AllocHGlobal(minSize);
                var created = new Entry { Ptr = ptr, Size = minSize, LastUsedGeneration = _generation };
                _all[ptr] = created;
                return ptr;
            }
        }

        public void Return(nint ptr)
        {
            if (ptr == 0)
                return;

            lock (_lock)
            {
                if (!_all.TryGetValue(ptr, out var entry))
                    return;

                entry.LastUsedGeneration = _generation;
                if (!_free.TryGetValue(entry.Size, out var queue))
                {
                    queue = new Queue<Entry>();
                    _free[entry.Size] = queue;
                }

                queue.Enqueue(entry);
            }
        }

        public void CleanupOldBuffers()
        {
            lock (_lock)
            {
                var threshold = _generation - _retentionGenerations;
                foreach (var queue in _free.Values)
                {
                    int count = queue.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var entry = queue.Dequeue();
                        if (entry.LastUsedGeneration < threshold)
                        {
                            Marshal.FreeHGlobal(entry.Ptr);
                            _all.Remove(entry.Ptr);
                        }
                        else
                        {
                            queue.Enqueue(entry);
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                foreach (var entry in _all.Values)
                    Marshal.FreeHGlobal(entry.Ptr);

                _all.Clear();
                _free.Clear();
                _disposed = true;
            }
        }
    }
}
