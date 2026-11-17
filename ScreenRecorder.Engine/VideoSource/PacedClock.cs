using System;
using System.Diagnostics;
using System.Threading;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.VideoSource
{
    /// <summary>
    /// Pure grid arithmetic for the paced clock: where the ideal tick <c>k</c> sits on the QPC
    /// axis and how many grid steps a late wake-up has to advance. Targets are always computed
    /// from the origin (never by accumulating a truncated interval), so there is no long-run
    /// drift at rates whose period is not a whole number of QPC ticks.
    /// </summary>
    internal readonly struct FrameGrid
    {
        private readonly long _origin;
        private readonly long _fpsNumerator;
        private readonly long _fpsDenominator;
        private readonly long _frequency;

        public FrameGrid(long origin, int fpsNumerator, int fpsDenominator, long frequency)
        {
            if (fpsNumerator <= 0) throw new ArgumentOutOfRangeException(nameof(fpsNumerator));
            if (fpsDenominator <= 0) throw new ArgumentOutOfRangeException(nameof(fpsDenominator));

            _origin = origin;
            _fpsNumerator = fpsNumerator;
            _fpsDenominator = fpsDenominator;
            _frequency = frequency;
        }

        /// <summary>QPC tick of grid index <paramref name="index"/> (relative to the origin).</summary>
        public long TicksAt(long index)
        {
            long units = index * _fpsDenominator;
            long whole = units / _fpsNumerator;
            long rem = units % _fpsNumerator;
            // Rounded up, so IndexAt(TicksAt(k)) == k exactly.
            return _origin + whole * _frequency + (rem * _frequency + _fpsNumerator - 1) / _fpsNumerator;
        }

        /// <summary>
        /// Largest grid index whose tick is ≤ <paramref name="now"/> (0 before the origin).
        /// </summary>
        public long IndexAt(long now)
        {
            long elapsed = now - _origin;
            if (elapsed <= 0)
                return 0;

            long whole = elapsed / _frequency;
            long rem = elapsed % _frequency;
            return whole * _fpsNumerator / _fpsDenominator
                   + (whole * _fpsNumerator % _fpsDenominator * _frequency + rem * _fpsNumerator) / (_fpsDenominator * _frequency);
        }
    }

    /// <summary>
    /// QPC ideal-grid pacer. Calls <c>onTick(k, missed)</c> once per wake-up with the current
    /// grid index <c>k</c> (PTS = k in a 1/fps time base — the ideal grid, NOT the wake time)
    /// and the number of grid indices skipped since the previous tick because the thread woke
    /// late. What happens to the skipped indices (repeat for CFR, skip for VFR) is the
    /// source's policy, not the clock's. Pausing holds the grid index and shifts the pacing
    /// origin, so paused time is excluded from the recording.
    /// </summary>
    internal sealed class PacedClock
    {
        private readonly int _fpsNumerator;
        private readonly int _fpsDenominator;
        private readonly long _t0;
        private readonly Action<long, int> _onTick;
        private long _lateTicks;
        private long _currentIndex = -1;
        private volatile bool _paused;

        public PacedClock(int fpsNumerator, int fpsDenominator, long t0, Action<long, int> onTick)
        {
            _fpsNumerator = fpsNumerator;
            _fpsDenominator = fpsDenominator;
            _t0 = t0;
            _onTick = onTick;
        }

        /// <summary>Grid indices skipped by late wake-ups (CFR fills them with repeats).</summary>
        public long LateTicks => Interlocked.Read(ref _lateTicks);

        /// <summary>The most recent grid index handed to onTick (-1 before the first tick).</summary>
        public long CurrentIndex => Interlocked.Read(ref _currentIndex);

        public void Pause() => _paused = true;

        public void Resume() => _paused = false;

        public void Run(CancellationToken ct)
        {
            using var waiter = new PrecisionWaiter();

            long pausedTotal = 0;   // QPC ticks spent paused, excluded from the grid
            long pauseStart = 0;
            long k = 0;
            long previous = -1;

            while (!ct.IsCancellationRequested)
            {
                if (_paused)
                {
                    if (pauseStart == 0)
                        pauseStart = Stopwatch.GetTimestamp();
                    Thread.Sleep(10);
                    continue;
                }

                if (pauseStart != 0)
                {
                    pausedTotal += Stopwatch.GetTimestamp() - pauseStart;
                    pauseStart = 0;
                }

                var grid = new FrameGrid(_t0 + pausedTotal, _fpsNumerator, _fpsDenominator, Stopwatch.Frequency);

                // Late: jump to the index the wall clock is actually at.
                long actual = grid.IndexAt(Stopwatch.GetTimestamp());
                if (actual > k)
                    k = actual;

                int missed = previous < 0 ? 0 : (int)Math.Min(int.MaxValue, k - previous - 1);
                if (missed > 0)
                    Interlocked.Add(ref _lateTicks, missed);

                Interlocked.Exchange(ref _currentIndex, k);
                _onTick(k, missed);

                previous = k;
                k++;
                waiter.WaitUntil(grid.TicksAt(k), ct);
            }
        }
    }
}
