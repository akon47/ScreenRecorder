namespace MediaEncoder
{
    /// <summary>
    /// Audio sample format. P1/P3 maps these onto FFmpeg <c>AVSampleFormat</c>.
    /// </summary>
    public enum SampleFormat
    {
        NONE = 0,
        U8,
        S16,
        S32,
        FLT,
        FLTP,
    }
}
