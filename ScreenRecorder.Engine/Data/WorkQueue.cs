using System;
using System.Threading;

namespace MediaEncoder
{
    /// <summary>
    /// Bounded FIFO hand-over queue between pipeline threads (capture → encode → mux). A fixed
    /// ring of value-type items guarded by a monitor: no allocation per item, and waiting uses
    /// Monitor.Wait/Pulse, which allocates nothing either. Producers choose per call whether a
    /// full queue drops (<see cref="TryEnqueue"/>, capture must never block) or applies
    /// back-pressure (<see cref="Enqueue"/>, encoded packets must not be lost).
    /// </summary>
    internal sealed class WorkQueue<T> where T : struct
    {
        private readonly object _lock = new object();
        private readonly T[] _items;
        private int _head;
        private int _count;
        private bool _completed;

        public WorkQueue(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new T[capacity];
        }

        public int Capacity => _items.Length;

        public int Count
        {
            get { lock (_lock) return _count; }
        }

        public bool IsCompleted
        {
            get { lock (_lock) return _completed; }
        }

        /// <summary>Adds without waiting. False when the queue is full or completed.</summary>
        public bool TryEnqueue(in T item)
        {
            lock (_lock)
            {
                if (_completed || _count == _items.Length)
                    return false;

                AddLocked(item);
                return true;
            }
        }

        /// <summary>Adds, waiting while the queue is full. False when the queue was completed.</summary>
        public bool Enqueue(in T item)
        {
            lock (_lock)
            {
                while (!_completed && _count == _items.Length)
                    Monitor.Wait(_lock);

                if (_completed)
                    return false;

                AddLocked(item);
                return true;
            }
        }

        /// <summary>
        /// Takes the next item, waiting while the queue is empty. Returns false only when the
        /// queue is completed AND drained — the consumer's signal to finish.
        /// </summary>
        public bool TryDequeue(out T item)
        {
            lock (_lock)
            {
                while (_count == 0 && !_completed)
                    Monitor.Wait(_lock);

                if (_count == 0)
                {
                    item = default;
                    return false;
                }

                item = _items[_head];
                _items[_head] = default;
                _head = (_head + 1) % _items.Length;
                _count--;
                Monitor.PulseAll(_lock); // wake a producer waiting for space
                return true;
            }
        }

        /// <summary>No more items will be added; consumers drain what is left, then stop.</summary>
        public void Complete()
        {
            lock (_lock)
            {
                _completed = true;
                Monitor.PulseAll(_lock);
            }
        }

        private void AddLocked(in T item)
        {
            _items[(_head + _count) % _items.Length] = item;
            _count++;
            Monitor.PulseAll(_lock);
        }
    }
}
