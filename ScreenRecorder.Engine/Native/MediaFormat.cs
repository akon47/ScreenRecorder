namespace MediaEncoder
{
    /// <summary>
    /// Container-format metadata. P0: a small static table for the five supported recording
    /// formats so the format combobox populates. P1 replaces this with an FFmpeg
    /// <c>av_guess_format</c> probe.
    /// </summary>
    public static class MediaFormat
    {
        public static void GetFormatInfo(string format, out string longName, out string extensions)
        {
            switch (format)
            {
                case "mp4": longName = "MP4"; extensions = "mp4"; return;
                case "avi": longName = "AVI"; extensions = "avi"; return;
                case "matroska": longName = "Matroska"; extensions = "mkv"; return;
                case "mpegts": longName = "MPEG-TS"; extensions = "ts"; return;
                case "mov": longName = "QuickTime"; extensions = "mov"; return;
                default: longName = null; extensions = null; return;
            }
        }
    }
}
