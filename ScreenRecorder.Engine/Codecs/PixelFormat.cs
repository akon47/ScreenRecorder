namespace MediaEncoder
{
    /// <summary>
    /// Pixel format for raw video frames. P1 maps these onto FFmpeg <c>AVPixelFormat</c>.
    /// </summary>
    public enum PixelFormat
    {
        None = 0,
        RGB24,
        BGR24,
        BGRA,
        NV12,
        YUV420P,
    }
}
