namespace MediaEncoder
{
    /// <summary>
    /// Hardware-encoder availability oracle. The four IsSupported* getters drive the shell's
    /// NotSupportedHwH264/Hevc UI flags. Probing actually opens the encoder at 1080p60
    /// (FFmpegHelper.IsEncoderUsable / avcodec_open2), so results are cached: CheckHardwareCodec()
    /// runs the four probes once (the shell calls it on a background task at startup).
    /// The actual encode/mux pipeline lives in Recorder + FFmpegFileContainer, not here.
    /// </summary>
    public static class MediaWriter
    {
        private static readonly object Gate = new object();
        private static bool _checked;
        private static bool _nvencH264;
        private static bool _qsvH264;
        private static bool _nvencHevc;
        private static bool _qsvHevc;

        public static void CheckHardwareCodec()
        {
            lock (Gate)
            {
                _nvencH264 = FFmpegHelper.IsEncoderUsable("h264_nvenc", "nvenc_h264");
                _qsvH264 = FFmpegHelper.IsEncoderUsable("h264_qsv");
                _nvencHevc = FFmpegHelper.IsEncoderUsable("hevc_nvenc", "nvenc_hevc");
                _qsvHevc = FFmpegHelper.IsEncoderUsable("hevc_qsv");
                _checked = true;
            }
        }

        public static bool IsSupportedNvencH264()
        {
            EnsureChecked();
            return _nvencH264;
        }

        public static bool IsSupportedQsvH264()
        {
            EnsureChecked();
            return _qsvH264;
        }

        public static bool IsSupportedNvencHEVC()
        {
            EnsureChecked();
            return _nvencHevc;
        }

        public static bool IsSupportedQsvHEVC()
        {
            EnsureChecked();
            return _qsvHevc;
        }

        private static void EnsureChecked()
        {
            if (!_checked)
                CheckHardwareCodec();
        }
    }
}
