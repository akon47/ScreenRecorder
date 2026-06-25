using System.Diagnostics;

namespace MediaEncoder
{
    /// <summary>
    /// Conversions between audio sample counts and QPC (Stopwatch) ticks, used to derive
    /// drift-free, sample-accurate timestamps. Split into whole-second and remainder terms to
    /// avoid overflow and rounding drift over long recordings.
    /// </summary>
    internal static class AudioHelper
    {
        public static long GetDurationFromSamples(long samples, int sampleRate)
        {
            return samples / sampleRate * Stopwatch.Frequency
                   + samples % sampleRate * Stopwatch.Frequency / sampleRate;
        }

        public static long GetSamplesFromTimeStamp(long ticks, int sampleRate)
        {
            return ticks / Stopwatch.Frequency * sampleRate
                   + ticks % Stopwatch.Frequency * sampleRate / Stopwatch.Frequency;
        }
    }
}
