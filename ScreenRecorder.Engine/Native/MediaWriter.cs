namespace MediaEncoder
{
    /// <summary>
    /// Hardware-encoder probe. P0: reports no hardware encoder available, so the shell's
    /// <c>NotSupportedHwH264</c>/<c>NotSupportedHwHevc</c> flags default to <c>true</c>
    /// (conservative). The real <c>avcodec_open2</c> @1080p60 probe + the encode/mux pipeline
    /// land in P1 (FFmpeg.AutoGen), replacing the legacy C++/CLI MediaWriter.
    /// </summary>
    public static class MediaWriter
    {
        public static void CheckHardwareCodec() { }

        public static bool IsSupportedNvencH264() => false;

        public static bool IsSupportedQsvH264() => false;

        public static bool IsSupportedNvencHEVC() => false;

        public static bool IsSupportedQsvHEVC() => false;
    }
}
