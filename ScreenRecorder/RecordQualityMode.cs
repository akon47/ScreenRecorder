namespace ScreenRecorder
{
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

    /// <summary>Combobox item pairing a <see cref="RecordQualityMode"/> with its localized name.</summary>
    public class RecordQualityModeItem
    {
        public RecordQualityMode Mode { get; }
        public string Name { get; }

        public RecordQualityModeItem(RecordQualityMode mode, string name)
        {
            Mode = mode;
            Name = name;
        }

        /// <summary>Constant-quality value (CRF/CQ, lower = better) for the quality modes.</summary>
        public static int ToQualityValue(RecordQualityMode mode)
        {
            switch (mode)
            {
                case RecordQualityMode.High: return 18;
                case RecordQualityMode.Low: return 28;
                default: return 23;
            }
        }
    }
}
