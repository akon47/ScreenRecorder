using System;
using System.Diagnostics;

namespace ScreenRecorder.Encoder
{
    /// <summary>
    /// Elapsed recording time, excluding paused time — what the UI shows while recording. It is
    /// independent of how many frames were encoded (in automatic frame-rate mode a static
    /// screen produces no frames, but time still passes). Timestamps are passed in so the logic
    /// is deterministic under test; the parameterless members use the real QPC clock.
    /// Thread-safe.
    /// </summary>
    public sealed class RecordingClock
    {
        private readonly object _lock = new object();
        private readonly long _frequency;
        private long _start;
        private long _pausedTotal;
        private long _pauseStart;
        private long _stop;
        private bool _running;
        private bool _paused;

        public RecordingClock() : this(Stopwatch.Frequency)
        {
        }

        public RecordingClock(long frequency)
        {
            if (frequency <= 0)
                throw new ArgumentOutOfRangeException(nameof(frequency));
            _frequency = frequency;
        }

        public bool IsRunning
        {
            get { lock (_lock) return _running; }
        }

        public bool IsPaused
        {
            get { lock (_lock) return _paused; }
        }

        public void Start() => Start(Stopwatch.GetTimestamp());

        public void Pause() => Pause(Stopwatch.GetTimestamp());

        public void Resume() => Resume(Stopwatch.GetTimestamp());

        public void Stop() => Stop(Stopwatch.GetTimestamp());

        public TimeSpan Elapsed => GetElapsed(Stopwatch.GetTimestamp());

        public void Start(long now)
        {
            lock (_lock)
            {
                _start = now;
                _pausedTotal = 0;
                _pauseStart = 0;
                _stop = 0;
                _running = true;
                _paused = false;
            }
        }

        public void Pause(long now)
        {
            lock (_lock)
            {
                if (!_running || _paused)
                    return;
                _paused = true;
                _pauseStart = now;
            }
        }

        public void Resume(long now)
        {
            lock (_lock)
            {
                if (!_running || !_paused)
                    return;
                _paused = false;
                _pausedTotal += Math.Max(0, now - _pauseStart);
            }
        }

        /// <summary>Freezes the clock; <see cref="GetElapsed"/> keeps returning the final value.</summary>
        public void Stop(long now)
        {
            lock (_lock)
            {
                if (!_running)
                    return;

                if (_paused)
                {
                    _pausedTotal += Math.Max(0, now - _pauseStart);
                    _paused = false;
                }

                _stop = now;
                _running = false;
            }
        }

        /// <summary>Back to zero (nothing recorded).</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _start = _stop = _pausedTotal = _pauseStart = 0;
                _running = _paused = false;
            }
        }

        public TimeSpan GetElapsed(long now)
        {
            long ticks;
            lock (_lock)
            {
                long end = _running ? (_paused ? _pauseStart : now) : _stop;
                ticks = end - _start - _pausedTotal;
            }

            if (ticks <= 0)
                return TimeSpan.Zero;

            // Whole seconds and remainder separately: no overflow, no precision loss.
            long seconds = ticks / _frequency;
            long remainder = ticks % _frequency;
            return TimeSpan.FromTicks(seconds * TimeSpan.TicksPerSecond + remainder * TimeSpan.TicksPerSecond / _frequency);
        }
    }
}
