namespace ScreenRecorder.Encoder
{
    /// <summary>
    /// Where the capture source hands its CPU frames: the video <see cref="Recorder"/> (NV12)
    /// or the <see cref="ScreenRecorder.Gif.GifRecorder"/> (BGRA). The contract is the same for
    /// both and never blocks the capture thread:
    /// rent a buffer → fill it → submit it with its grid index (ownership passes to the sink
    /// whether or not the frame is accepted). A zero rent or a false submit means the sink is
    /// behind and the frame was dropped.
    /// </summary>
    public interface IVideoFrameSink
    {
        /// <summary>A buffer of at least <paramref name="bytes"/> bytes, or 0 when the frame must be skipped.</summary>
        nint RentVideoBuffer(int bytes);

        /// <summary>Gives back a rented buffer that will not be submitted.</summary>
        void ReturnVideoBuffer(nint buffer);

        /// <summary>
        /// Queues a filled buffer as the picture for <paramref name="gridIndex"/>. Returns false
        /// when the frame was dropped.
        /// </summary>
        bool SubmitVideoBuffer(nint buffer, int stride, int bytes, long gridIndex);

        /// <summary>
        /// The previous picture is (still) the picture at <paramref name="gridIndex"/>. Returns
        /// false when there is no previous picture or the index is not later than the last one.
        /// </summary>
        bool RepeatLastVideoFrame(long gridIndex);
    }
}
