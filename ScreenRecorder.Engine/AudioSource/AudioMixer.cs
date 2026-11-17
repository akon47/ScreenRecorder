using System;

namespace ScreenRecorder.AudioSource
{
    /// <summary>
    /// Mixes up to two source timelines (system loopback and/or microphone — either may be
    /// absent) into a fixed S16 interleaved block: float sum, clamp to [-1,1], scale to S16.
    /// Operates on correctly-centered float straight from the resampler — no signed→unsigned
    /// bias (the legacy MixStereoSamples subtracted 32768, DC-shifting silence and corrupting
    /// the level gate). With no source at all the block is silence.
    /// </summary>
    internal sealed class AudioMixer
    {
        private const int Channels = 2;

        private readonly SourceTimeline _first;
        private readonly SourceTimeline _second;
        private float[] _firstFloat = Array.Empty<float>();
        private float[] _secondFloat = Array.Empty<float>();

        public AudioMixer(SourceTimeline loopback, SourceTimeline mic)
        {
            // Order is irrelevant to the sum; compact so the single-source case is the fast path.
            _first = loopback ?? mic;
            _second = loopback != null ? mic : null;
        }

        public void MixBlock(short[] outS16, int frames)
        {
            int count = frames * Channels;

            if (_first == null)
            {
                Array.Clear(outS16, 0, count);
                return;
            }

            if (_firstFloat.Length < count)
                _firstFloat = new float[count];

            _first.Read(_firstFloat.AsSpan(0, count), frames);

            if (_second != null)
            {
                if (_secondFloat.Length < count)
                    _secondFloat = new float[count];
                _second.Read(_secondFloat.AsSpan(0, count), frames);

                for (int i = 0; i < count; i++)
                    outS16[i] = ToS16(_firstFloat[i] + _secondFloat[i]);
            }
            else
            {
                for (int i = 0; i < count; i++)
                    outS16[i] = ToS16(_firstFloat[i]);
            }
        }

        internal static short ToS16(float s)
        {
            if (s > 1f) s = 1f;
            else if (s < -1f) s = -1f;
            else if (float.IsNaN(s)) s = 0f;
            return (short)(s * 32767f);
        }
    }
}
