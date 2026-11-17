using System;

namespace ScreenRecorder.Tests.Support
{
    /// <summary>
    /// Interleaved sine tones (L = 440 Hz, R = 445 Hz) with continuous phase across blocks,
    /// peak amplitude 0.3 of full scale. Adapted from the encoder harness.
    /// </summary>
    internal sealed class SinePcm
    {
        public const double Amplitude = 0.3;

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
            for (int i = 0; i < blockSamples; i++)
            {
                Next(out double left, out double right);
                for (int c = 0; c < _channels; c++)
                    dst[i * _channels + c] = (short)((c == 1 ? right : left) * short.MaxValue);
            }
        }

        public void Fill(float[] dst, int blockSamples)
        {
            for (int i = 0; i < blockSamples; i++)
            {
                Next(out double left, out double right);
                for (int c = 0; c < _channels; c++)
                    dst[i * _channels + c] = (float)(c == 1 ? right : left);
            }
        }

        private void Next(out double left, out double right)
        {
            left = Math.Sin(_phaseL) * Amplitude;
            right = Math.Sin(_phaseR) * Amplitude;
            _phaseL += 2.0 * Math.PI * 440.0 / _sampleRate;
            _phaseR += 2.0 * Math.PI * 445.0 / _sampleRate;
            if (_phaseL > 2.0 * Math.PI) _phaseL -= 2.0 * Math.PI;
            if (_phaseR > 2.0 * Math.PI) _phaseR -= 2.0 * Math.PI;
        }
    }
}
