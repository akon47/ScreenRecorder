namespace ScreenRecorder.Models
{
    /// <summary>A value paired with the text a list shows for it.</summary>
    public sealed class OptionItem<T>
    {
        public T Value { get; }

        public string Name { get; }

        public OptionItem(T value, string name)
        {
            Value = value;
            Name = name ?? string.Empty;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Rate-control choice shown in the (advanced) encoder settings: fixed bitrate (CBR, the
    /// long-standing default) or constant quality, where the bitrate follows content complexity
    /// (CRF on software encoders, CQ on NVENC). Quality levels map to the 0–51 scale.
    /// </summary>
    public enum RecordQualityMode
    {
        Bitrate,
        High,
        Medium,
        Low,
    }

    public static class RecordQualityModeExtensions
    {
        /// <summary>Constant-quality value (CRF/CQ, lower = better) for the quality modes.</summary>
        public static int ToQualityValue(this RecordQualityMode mode)
        {
            switch (mode)
            {
                case RecordQualityMode.High: return 18;
                case RecordQualityMode.Low: return 28;
                default: return 23;
            }
        }
    }

    /// <summary>A container format the recorder can write.</summary>
    public sealed class ContainerFormat
    {
        /// <summary>FFmpeg muxer short name ("mp4", "matroska", ...).</summary>
        public string Format { get; }

        /// <summary>Display name ("MP4", "Matroska", ...).</summary>
        public string Name { get; }

        /// <summary>File extension without the dot ("mp4", "mkv", ...).</summary>
        public string Extension { get; }

        public ContainerFormat(string format, string name, string extension)
        {
            Format = format;
            Name = name;
            Extension = extension;
        }

        /// <summary>Looks the format up in the engine's format table; null when unknown.</summary>
        public static ContainerFormat FromFormat(string format)
        {
            MediaEncoder.MediaFormat.GetFormatInfo(format, out string longName, out string extensions);
            if (string.IsNullOrWhiteSpace(longName) || string.IsNullOrWhiteSpace(extensions))
                return null;

            string extension = extensions.Split(',')[0].Trim();
            return new ContainerFormat(format, longName, extension);
        }

        public override string ToString() => Name;
    }
}
