using System;

namespace ScreenRecorder.Encoder
{
    /// <summary>
    /// Frame-rate selection. A configured rate of <see cref="Auto"/> (0) means "record only the
    /// frames that actually changed": the capture grid runs at a rate derived from the monitor's
    /// refresh rate and a frame is encoded only when the screen content changed (variable frame
    /// rate on the ideal grid). Any positive rate is a fixed constant-frame-rate recording.
    /// Pure logic — unit tested.
    /// </summary>
    public static class AutoFrameRate
    {
        /// <summary>The "automatic" frame-rate setting value.</summary>
        public const int Auto = 0;

        /// <summary>Grid rate used when the monitor refresh rate is unknown.</summary>
        public const int DefaultGridRate = 60;

        public static bool IsAuto(int configuredFrameRate) => configuredFrameRate <= 0;

        /// <summary>
        /// Rounds driver-reported NTSC-style rates up to their nominal value (59 → 60,
        /// 119 → 120, 143 → 144, 23 → 24 ...). Unknown / "hardware default" (0, 1) → 60.
        /// </summary>
        public static int NormalizeRefreshRate(int refreshHz)
        {
            if (refreshHz <= 1)
                return DefaultGridRate;

            int next = refreshHz + 1;
            if (next % 24 == 0 || next % 30 == 0 || next % 25 == 0 && next % 50 == 0)
                return next;

            return refreshHz;
        }

        /// <summary>
        /// The capture grid rate for automatic mode: the largest integer divisor of the refresh
        /// rate that lands near 60 fps, so the grid samples whole monitor frames (no judder) while
        /// staying at a sensible encode load — 60 → 60, 75 → 75, 120 → 60, 144 → 72, 165 → 55,
        /// 240 → 60.
        /// </summary>
        public static int FromRefreshRate(int refreshHz)
        {
            int hz = NormalizeRefreshRate(refreshHz);
            if (hz <= 90)
                return Math.Max(15, hz);

            int divisor = Math.Max(1, (int)Math.Round(hz / 60.0, MidpointRounding.AwayFromZero));
            int rate = (int)Math.Round(hz / (double)divisor, MidpointRounding.AwayFromZero);
            return Math.Clamp(rate, 30, 90);
        }

        /// <summary>The grid rate to run: the configured fixed rate, or the automatic rate.</summary>
        public static int ResolveGridRate(int configuredFrameRate, int refreshHz)
        {
            return IsAuto(configuredFrameRate) ? FromRefreshRate(refreshHz) : configuredFrameRate;
        }
    }
}
