using System;

namespace ScreenRecorder.AudioSource
{
    /// <summary>
    /// Per-source ring buffer of 48k/2ch interleaved float samples, decoupling a capture's own
    /// callback cadence from the mixer's fixed-block pull. Single producer (capture thread) /
    /// single consumer (audio push thread). Read silence-fills on underrun — the normal idle case
    /// (e.g. loopback delivers nothing while no audio plays), so the output stream never stalls.
    /// </summary>
    internal sealed class SourceTimeline
    {
        private const int Channels = 2;

        private readonly object _lock = new object();
        private readonly float[] _ring; // interleaved, capacity in floats
        private readonly int _capacityFrames;
        private int _writePos;  // float index
        private int _readPos;   // float index
        private int _availFrames;
        private long _overflowFrames;
        private long _underrunFrames;

        public SourceTimeline(int capacityFrames = 1024 * 16)
        {
            if (capacityFrames < 1)
                throw new ArgumentOutOfRangeException(nameof(capacityFrames));

            _capacityFrames = capacityFrames;
            _ring = new float[capacityFrames * Channels];
        }

        public int CapacityFrames => _capacityFrames;

        /// <summary>Frames currently buffered.</summary>
        public int AvailableFrames
        {
            get { lock (_lock) return _availFrames; }
        }

        /// <summary>Frames discarded because the consumer stalled (oldest dropped).</summary>
        public long OverflowFrames
        {
            get { lock (_lock) return _overflowFrames; }
        }

        /// <summary>Frames of silence substituted for missing data.</summary>
        public long UnderrunFrames
        {
            get { lock (_lock) return _underrunFrames; }
        }

        public void Write(ReadOnlySpan<float> interleaved, int frames)
        {
            if (frames <= 0)
                return;

            // More than the whole ring at once: only the newest part can survive.
            if (frames > _capacityFrames)
            {
                int skip = frames - _capacityFrames;
                interleaved = interleaved.Slice(skip * Channels);
                frames = _capacityFrames;
                lock (_lock)
                    _overflowFrames += skip;
            }

            int count = frames * Channels;
            lock (_lock)
            {
                // Drop oldest on overflow (severe stall only).
                if (_availFrames + frames > _capacityFrames)
                {
                    int overflow = _availFrames + frames - _capacityFrames;
                    _readPos = (_readPos + overflow * Channels) % _ring.Length;
                    _availFrames -= overflow;
                    _overflowFrames += overflow;
                }

                int first = Math.Min(count, _ring.Length - _writePos);
                interleaved.Slice(0, first).CopyTo(_ring.AsSpan(_writePos, first));
                if (first < count)
                    interleaved.Slice(first, count - first).CopyTo(_ring.AsSpan(0, count - first));

                _writePos = (_writePos + count) % _ring.Length;
                _availFrames += frames;
            }
        }

        /// <summary>Pops <paramref name="frames"/> frames into dst; silence-fills any shortfall.
        /// Returns the number of frames actually backed by real data.</summary>
        public int Read(Span<float> dst, int frames)
        {
            int count = frames * Channels;
            lock (_lock)
            {
                int realFrames = Math.Min(frames, _availFrames);
                int realCount = realFrames * Channels;

                int first = Math.Min(realCount, _ring.Length - _readPos);
                _ring.AsSpan(_readPos, first).CopyTo(dst);
                if (first < realCount)
                    _ring.AsSpan(0, realCount - first).CopyTo(dst.Slice(first));

                _readPos = (_readPos + realCount) % _ring.Length;
                _availFrames -= realFrames;

                if (realCount < count)
                {
                    dst.Slice(realCount, count - realCount).Clear(); // silence-fill underrun
                    _underrunFrames += frames - realFrames;
                }

                return realFrames;
            }
        }

        /// <summary>Discards everything buffered (the source restarted).</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _readPos = _writePos = 0;
                _availFrames = 0;
            }
        }
    }
}
