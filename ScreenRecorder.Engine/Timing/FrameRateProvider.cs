namespace ScreenRecorder.Encoder
{
    /// <summary>
    /// P0 replacement for the legacy static <c>VideoClockEvent.Framerate</c> holder — a plain
    /// int, with no pulse thread. The real timing model (single QPC <c>t0</c>, ideal-grid CFR
    /// PTS, per-frame/packet timestamps, precise pacing) lands in P2/P3 per CLAUDE.md.
    /// </summary>
    public static class FrameRateProvider
    {
        public static int Framerate { get; set; } = 60;
    }
}
