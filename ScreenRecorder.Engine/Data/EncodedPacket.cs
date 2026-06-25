using System;

namespace MediaEncoder
{
    /// <summary>
    /// An encoded packet handed from an encoder to the muxer. <see cref="Data"/> points at a
    /// buffer that is only valid until the next encode call (codec-owned, reused) — the encode
    /// worker re-copies it into a pooled buffer before crossing the thread boundary.
    /// </summary>
    public readonly struct EncodedVideoPacket
    {
        public readonly long PresentationTimeStamp;
        public readonly long DecodingTimeStamp;
        public readonly nint Data;
        public readonly int Size;
        public readonly bool IsKeyFrame;

        public EncodedVideoPacket(long pts, long dts, nint data, int size, bool isKeyFrame)
        {
            PresentationTimeStamp = pts;
            DecodingTimeStamp = dts;
            Data = data;
            Size = size;
            IsKeyFrame = isKeyFrame;
        }
    }

    public readonly struct EncodedAudioPacket
    {
        public readonly long PresentationTimeStamp;
        public readonly long DecodingTimeStamp;
        public readonly nint Data;
        public readonly int Size;

        public EncodedAudioPacket(long pts, long dts, nint data, int size)
        {
            PresentationTimeStamp = pts;
            DecodingTimeStamp = dts;
            Data = data;
            Size = size;
        }
    }
}
