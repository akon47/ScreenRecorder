using System;
using System.Diagnostics;
using System.Threading;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.VideoSource
{
    /// <summary>
    /// QPC ideal-grid CFR pacer (ported from Aurora's VideoOutputProcessor). Calls onTick exactly
    /// once per grid index k with PTS = t0 + k*interval (the ideal grid, NOT wake time). On a late
    /// tick it emits one DUPLICATE per skipped index so the encoder receives exactly fps×duration
    /// frames — the confirmed CFR contract (Aurora skips late indices; the recorder must not).
    /// </summary>
    internal sealed class PacedClock
    {
        private readonly long _interval;
        private readonly long _t0;
        private readonly Action<long, long, bool> _onTick;
        private long _dropped;
        private volatile bool _paused;
        private long _pauseStart;

        public PacedClock(int fpsNumerator, int fpsDenominator, long t0, Action<long, long, bool> onTick)
        {
            _interval = Stopwatch.Frequency * fpsDenominator / fpsNumerator;
            _t0 = t0;
            _onTick = onTick;
        }

        public long DroppedFrames => Interlocked.Read(ref _dropped);

        public void Pause() => _paused = true;

        public void Resume() => _paused = false;

        public void Run(CancellationToken ct)
        {
            long lastFrameTicks = _t0;
            long k = 0;

            while (!ct.IsCancellationRequested)
            {
                // Paused: hold the grid index (paused time is excluded from the recording) and
                // shift the wall-clock pacing reference forward on resume so no burst follows.
                if (_paused)
                {
                    if (_pauseStart == 0)
                        _pauseStart = Stopwatch.GetTimestamp();
                    Thread.Sleep(10);
                    continue;
                }
                if (_pauseStart != 0)
                {
                    lastFrameTicks += Stopwatch.GetTimestamp() - _pauseStart;
                    _pauseStart = 0;
                }

                _onTick(_t0 + k * _interval, k, false);

                long target = lastFrameTicks + _interval;
                if (QpcSleep.SleepToTicks(target, ct))
                {
                    k++;
                    lastFrameTicks = target;
                }
                else
                {
                    long diff = Math.Max(Stopwatch.GetTimestamp() - lastFrameTicks, _interval);
                    long count = diff / _interval; // >= 1
                    for (long i = 1; i < count; i++)
                        _onTick(_t0 + (k + i) * _interval, k + i, true); // duplicate held frame

                    Interlocked.Add(ref _dropped, count - 1);
                    k += count;
                    lastFrameTicks += _interval * count;
                }
            }
        }

    }
}
