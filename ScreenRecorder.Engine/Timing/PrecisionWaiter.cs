using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScreenRecorder.Encoder
{
    /// <summary>
    /// Waits until a QPC (Stopwatch) target tick using a high-resolution waitable timer
    /// (Windows 10 1803+), which wakes within ~0.5 ms without raising the global timer
    /// resolution and without busy-spinning a core. Frame and audio timestamps come from the
    /// ideal grid / sample count, not from the wake-up time, so sub-millisecond wake jitter is
    /// irrelevant — burning CPU in a spin loop buys nothing. On older systems it falls back to
    /// a normal waitable timer under <c>timeBeginPeriod(1)</c>.
    /// One instance per pacing thread (the timer handle is not shared).
    /// </summary>
    internal sealed class PrecisionWaiter : IDisposable
    {
        private const uint CreateWaitableTimerHighResolution = 0x00000002;
        private const uint TimerAllAccess = 0x1F0003;
        private const uint WaitTimeoutSlackMs = 20;

        /// <summary>Longest single wait; keeps cancellation latency bounded.</summary>
        private const long MaxChunk100Ns = 50 * 10_000;

        private nint _timer;
        private bool _periodRaised;
        private bool _disposed;

        public bool IsHighResolution { get; }

        public PrecisionWaiter()
        {
            _timer = CreateWaitableTimerExW(0, 0, CreateWaitableTimerHighResolution, TimerAllAccess);
            IsHighResolution = _timer != 0;
            if (_timer == 0)
            {
                _timer = CreateWaitableTimerExW(0, 0, 0, TimerAllAccess);
                _periodRaised = timeBeginPeriod(1) == 0;
            }
        }

        /// <summary>
        /// Blocks until <paramref name="targetTicks"/> (Stopwatch ticks). Returns false when the
        /// target had already passed on entry (the caller is running late), true otherwise.
        /// </summary>
        public bool WaitUntil(long targetTicks, CancellationToken ct)
        {
            long remaining = targetTicks - Stopwatch.GetTimestamp();
            if (remaining <= 0)
                return false;

            while (remaining > 0 && !ct.IsCancellationRequested)
            {
                long due100Ns = Math.Min(ToHundredNanoseconds(remaining), MaxChunk100Ns);
                if (due100Ns <= 0)
                    break;

                if (_timer != 0)
                {
                    long dueTime = -due100Ns; // negative = relative
                    if (SetWaitableTimer(_timer, ref dueTime, 0, 0, 0, false))
                        WaitForSingleObject(_timer, (uint)(due100Ns / 10_000) + WaitTimeoutSlackMs);
                    else
                        Thread.Sleep((int)Math.Max(1, due100Ns / 10_000));
                }
                else
                {
                    Thread.Sleep((int)Math.Max(1, due100Ns / 10_000));
                }

                remaining = targetTicks - Stopwatch.GetTimestamp();
            }

            return true;
        }

        internal static long ToHundredNanoseconds(long stopwatchTicks)
        {
            // Stopwatch.Frequency is 10 MHz on Windows (already 100 ns units); stay general.
            long freq = Stopwatch.Frequency;
            return freq == 10_000_000 ? stopwatchTicks : (long)(stopwatchTicks * (10_000_000.0 / freq));
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_timer != 0)
            {
                CloseHandle(_timer);
                _timer = 0;
            }

            if (_periodRaised)
            {
                timeEndPeriod(1);
                _periodRaised = false;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern nint CreateWaitableTimerExW(nint lpTimerAttributes, nint lpTimerName, uint dwFlags, uint dwDesiredAccess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(nint hTimer, ref long lpDueTime, int lPeriod, nint pfnCompletionRoutine, nint lpArgToCompletionRoutine, [MarshalAs(UnmanagedType.Bool)] bool fResume);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(nint hObject);

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);
    }
}
