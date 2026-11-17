namespace MediaEncoder
{
    /// <summary>
    /// Display name and file extension of each output format the recorder can write, keyed by
    /// the format's short name (the FFmpeg muxer name where FFmpeg writes it).
    /// </summary>
    public static class MediaFormat
    {
        /// <summary>
        /// Whether <paramref name="format"/> can hold <paramref name="codec"/>. H.264 and H.265
        /// go everywhere; AV1 only into the containers with a settled, widely played mapping.
        /// </summary>
        public static bool SupportsVideoCodec(string format, VideoCodec codec)
        {
            if (codec != VideoCodec.Av1)
                return true;

            return format == "mp4" || format == "matroska";
        }

        public static void GetFormatInfo(string format, out string longName, out string extensions)
        {
            switch (format)
            {
                case "mp4": longName = "MP4"; extensions = "mp4"; return;
                case "avi": longName = "AVI"; extensions = "avi"; return;
                case "matroska": longName = "Matroska"; extensions = "mkv"; return;
                case "mpegts": longName = "MPEG-TS"; extensions = "ts"; return;
                case "mov": longName = "QuickTime"; extensions = "mov"; return;
                // Audio-only recordings.
                case "ipod": longName = "M4A (AAC)"; extensions = "m4a"; return;
                case "mp3": longName = "MP3"; extensions = "mp3"; return;
                // Animated GIF (written by the engine's own GIF encoder, not by FFmpeg).
                case "gif": longName = "GIF"; extensions = "gif"; return;
                default: longName = null; extensions = null; return;
            }
        }
    }
}
