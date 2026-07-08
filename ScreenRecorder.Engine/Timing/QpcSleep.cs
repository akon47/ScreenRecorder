using System.Diagnostics;
using System.Runtime.Intrinsics.X86;
using System.Threading;

namespace ScreenRecorder.Encoder
{
    /// <summary>
    /// Hybrid coarse-sleep + busy-spin to a precise QPC (Stopwatch) target tick. Shared by the
    /// video paced clock and the audio push loop so both pace to the same precision primitive.
    /// </summary>
    internal static class QpcSleep
    {
        /// <summary>Waits until <paramref name="targetTicks"/>. Returns true if it actually waited.</summary>
        public static bool SleepToTicks(long targetTicks, CancellationToken ct)
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
