using System;

namespace ScreenRecorder.EncoderHarness
{
    /// <summary>
    /// Generates interleaved S16 stereo sine tones (L = 440 Hz, R = 445 Hz so channels are
    /// distinguishable) with continuous phase across blocks (no clicks).
    /// </summary>
    internal sealed class SinePcm
    {
        private readonly int _sampleRate;
        private readonly int _channels;
        private double _phaseL;
        private double _phaseR;

        public SinePcm(int sampleRate, int channels)
        {
            _sampleRate = sampleRate;
            _channels = channels;
        }

        public void Fill(short[] dst, int blockSamples)
        {
            const double amplitude = 0.3 * short.MaxValue;
            double stepL = 2.0 * Math.PI * 440.0 / _sampleRate;
            double stepR = 2.0 * Math.PI * 445.0 / _sampleRate;

            for (int i = 0; i < blockSamples; i++)
            {
                short left = (short)(Math.Sin(_phaseL) * amplitude);
                short right = (short)(Math.Sin(_phaseR) * amplitude);
                _phaseL += stepL;
                _phaseR += stepR;

                if (_channels == 1)
                {
                    dst[i] = left;
                }
                else
                {
                    dst[i * _channels] = left;
                    dst[i * _channels + 1] = right;
                    for (int c = 2; c < _channels; c++)
                        dst[i * _channels + c] = left;
                }
            }

            if (_phaseL > 2.0 * Math.PI * 1000) _phaseL -= 2.0 * Math.PI * 1000;
            if (_phaseR > 2.0 * Math.PI * 1000) _phaseR -= 2.0 * Math.PI * 1000;
        }
    }
}
