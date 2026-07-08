namespace MediaEncoder
{
    /// <summary>
    /// Video codec selection. Namespace kept as <c>MediaEncoder</c> (was the C++/CLI assembly)
    /// so the shell + XAML (<c>x:Static mediaEncoder:VideoCodec.H264</c>) and saved-config
    /// strings (persisted via <see cref="System.Enum.GetName"/>) resolve unchanged.
    /// </summary>
    public enum VideoCodec
    {
        None = 0,
        Default = 1,
        H264 = 2,
        Hevc = 3,
        H265 = Hevc,
    }
}
