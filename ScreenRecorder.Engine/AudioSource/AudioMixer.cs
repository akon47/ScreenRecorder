using System;

namespace ScreenRecorder.AudioSource
{
    /// <summary>
    /// Mixes the loopback timeline (always) and an optional mic timeline into a fixed S16
    /// interleaved block: float sum, clamp to [-1,1], scale to S16. Operates on correctly-centered
    /// float straight from the resampler — no signed→unsigned bias (the legacy MixStereoSamples
    /// subtracted 32768, DC-shifting silence and corrupting the level gate).
    /// </summary>
    internal sealed class AudioMixer
    {
        private const int Channels = 2;

        private readonly SourceTimeline _loopback;
        private readonly SourceTimeline _mic;
        private float[] _loopFloat = Array.Empty<float>();
        private float[] _micFloat = Array.Empty<float>();

        public AudioMixer(SourceTimeline loopback, SourceTimeline mic)
        {
            _loopback = loopback;
            _mic = mic;
        }

        public void MixBlock(short[] outS16, int frames)
        {
            int count = frames * Channels;
            if (_loopFloat.Length < count)
                _loopFloat = new float[count];

            _loopback.Read(_loopFloat.AsSpan(0, count), frames);

            if (_mic != null)
            {
                if (_micFloat.Length < count)
                    _micFloat = new float[count];
                _mic.Read(_micFloat.AsSpan(0, count), frames);

                for (int i = 0; i < count; i++)
                    outS16[i] = ToS16(_loopFloat[i] + _micFloat[i]);
            }
            else
            {
                for (int i = 0; i < count; i++)
                    outS16[i] = ToS16(_loopFloat[i]);
            }
        }

        private static short ToS16(float s)
        {
            if (s > 1f) s = 1f;
            else if (s < -1f) s = -1f;
            return (short)(s * 32767f);
        }
    }
}
