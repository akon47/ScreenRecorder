using System;
using System.Threading;

namespace ScreenRecorder.AudioSource
{
    /// <summary>
    /// Remembers the loudest sample that went into the recording since it was last asked.
    /// The audio thread adds every block it writes; the user interface reads at its own pace
    /// (a reader that polls slower than blocks arrive still sees every peak, because the
    /// maximum is kept until it is read). Lock-free and allocation-free.
    /// </summary>
    public sealed class AudioPeakMeter
    {
        /// <summary>Level reported for silence, and the bottom of the meter scale.</summary>
        public const double FloorDecibels = -60.0;

        private const float FullScale = 32768f;

        private int _peak; // largest |sample| since the last Read, 0..32768

        /// <summary>Adds a block of 16-bit samples (any channel layout: the peak is over all of them).</summary>
        public void Add(ReadOnlySpan<short> samples)
        {
            int max = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                int value = samples[i];
                if (value < 0)
                    value = -value; // -32768 becomes 32768: fine in an int
                if (value > max)
                    max = value;
            }

            if (max == 0)
                return;

            // Keep the larger of the stored and the new peak.
            int seen = Volatile.Read(ref _peak);
            while (max > seen)
            {
                int previous = Interlocked.CompareExchange(ref _peak, max, seen);
                if (previous == seen)
                    break;
                seen = previous;
            }
        }

        /// <summary>The peak since the previous call as a linear level 0..1, and starts over.</summary>
        public float Read() => Interlocked.Exchange(ref _peak, 0) / FullScale;

        /// <summary>Forgets what was added (start of a recording).</summary>
        public void Reset() => Interlocked.Exchange(ref _peak, 0);

        /// <summary>
        /// A linear level as decibels relative to full scale: 1 → 0 dB, 0.5 → about −6 dB;
        /// silence and anything quieter than the floor → <see cref="FloorDecibels"/>.
        /// </summary>
        public static double ToDecibels(float level)
        {
            if (!(level > 0f))
                return FloorDecibels;

            double decibels = 20.0 * Math.Log10(level);
            if (decibels < FloorDecibels)
                return FloorDecibels;
            return decibels > 0.0 ? 0.0 : decibels;
        }
    }
}
