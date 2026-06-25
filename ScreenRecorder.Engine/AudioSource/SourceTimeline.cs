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

        public SourceTimeline(int capacityFrames = 1024 * 8)
        {
            _capacityFrames = capacityFrames;
            _ring = new float[capacityFrames * Channels];
        }

        public void Write(ReadOnlySpan<float> interleaved, int frames)
        {
            int count = frames * Channels;
            lock (_lock)
            {
                // Drop oldest on overflow (severe stall only).
                if (_availFrames + frames > _capacityFrames)
                {
                    int overflowFrames = _availFrames + frames - _capacityFrames;
                    _readPos = (_readPos + overflowFrames * Channels) % _ring.Length;
                    _availFrames -= overflowFrames;
                }

                for (int i = 0; i < count; i++)
                {
                    _ring[_writePos] = interleaved[i];
                    _writePos = (_writePos + 1) % _ring.Length;
                }
                _availFrames += frames;
            }
        }

        /// <summary>Pops <paramref name="frames"/> frames into dst; silence-fills any shortfall.
        /// Returns the number of frames actually backed by real data (stats only).</summary>
        public int Read(Span<float> dst, int frames)
        {
            int count = frames * Channels;
            lock (_lock)
            {
                int realFrames = Math.Min(frames, _availFrames);
                int realCount = realFrames * Channels;

                for (int i = 0; i < realCount; i++)
                {
                    dst[i] = _ring[_readPos];
                    _readPos = (_readPos + 1) % _ring.Length;
                }
                _availFrames -= realFrames;

                for (int i = realCount; i < count; i++)
                    dst[i] = 0f; // silence-fill underrun

                return realFrames;
            }
        }
    }
}
