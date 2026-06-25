using System;

namespace MediaEncoder
{
    /// <summary>
    /// A raw (un-encoded) video frame pushed into the recorder. <see cref="Data"/> is CPU memory
    /// owned by the producer; the encoder copies it synchronously, so the producer may reuse the
    /// buffer after the push returns. <see cref="TimestampQpc"/> is a QPC (Stopwatch) tick value
    /// from the shared recording origin t0.
    /// </summary>
    public readonly struct RawVideoFrame
    {
        public readonly nint Data;
        public readonly int Stride;
        public readonly int Width;
        public readonly int Height;
        public readonly PixelFormat Format;
        public readonly long TimestampQpc;

        public RawVideoFrame(nint data, int stride, int width, int height, PixelFormat format, long timestampQpc)
        {
            Data = data;
            Stride = stride;
            Width = width;
            Height = height;
            Format = format;
            TimestampQpc = timestampQpc;
        }
    }

    /// <summary>
    /// A raw interleaved PCM audio block pushed into the recorder. <see cref="Data"/> is CPU
    /// memory owned by the producer (copied synchronously on push).
    /// </summary>
    public readonly struct RawAudioFrame
    {
        public readonly nint Data;
        public readonly int Samples;
        public readonly int SampleRate;
        public readonly int Channels;
        public readonly SampleFormat Format;
        public readonly long TimestampQpc;

        public RawAudioFrame(nint data, int samples, int sampleRate, int channels, SampleFormat format, long timestampQpc)
        {
            Data = data;
            Samples = samples;
            SampleRate = sampleRate;
            Channels = channels;
            Format = format;
            TimestampQpc = timestampQpc;
        }
    }
}
