using System;
using System.Diagnostics;
using System.Runtime.Intrinsics.X86;
using System.Threading;

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

        public PacedClock(int fpsNumerator, int fpsDenominator, long t0, Action<long, long, bool> onTick)
        {
            _interval = Stopwatch.Frequency * fpsDenominator / fpsNumerator;
            _t0 = t0;
            _onTick = onTick;
        }

        public long DroppedFrames => Interlocked.Read(ref _dropped);

        public void Run(CancellationToken ct)
        {
            long lastFrameTicks = _t0;
            long k = 0;

            while (!ct.IsCancellationRequested)
            {
                _onTick(_t0 + k * _interval, k, false);

                long target = lastFrameTicks + _interval;
                if (SleepToTicks(target, ct))
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

        /// <summary>Hybrid coarse-sleep + busy-spin to the exact QPC target. Returns true if it waited.</summary>
        private static bool SleepToTicks(long targetTicks, CancellationToken ct)
        {
            long now = Stopwatch.GetTimestamp();
            bool waited = now < targetTicks;
            if (!waited)
                return false;

            int ms = (int)((targetTicks - now) * 1000.0 / Stopwatch.Frequency);
            if (ms > 1)
                Thread.Sleep(ms - 1);

            while (Stopwatch.GetTimestamp() < targetTicks)
            {
                if (ct.IsCancellationRequested)
                    break;
                if (X86Base.IsSupported)
                    X86Base.Pause();
                else
                    Thread.SpinWait(1);
            }

            return true;
        }
    }
}
