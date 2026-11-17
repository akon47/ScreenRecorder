using System;

namespace ScreenRecorder.AudioSource
{
    /// <summary>
    /// Computes the resampling-ratio correction (ppm) that keeps a source's buffer fill at a
    /// target. An audio device runs on its own crystal, a few tens of ppm off the recording's
    /// QPC clock: uncorrected, the buffer slowly drains (→ silence has to be inserted) or grows
    /// (→ samples have to be dropped), and every such event is an audible tick. Instead the
    /// ratio is nudged by a tiny amount proportional to the fill error, so the stream follows
    /// the device without a discontinuity. Fill saw-tooths with packet arrival, so only the
    /// MINIMUM over an observation window is looked at, and the correction is updated when the
    /// window closes. No I/O — unit tested directly.
    /// </summary>
    internal sealed class AudioRateServo
    {
        /// <summary>Correction limit. 0.1 % is far below an audible pitch change.</summary>
        public const int MaxCompensationPpm = 1000;

        /// <summary>Observation window; much longer than a capture packet (≤ tens of ms).</summary>
        private const double WindowSeconds = 1.0;

        /// <summary>Time over which a fill error is worked off; longer = gentler ratio changes.</summary>
        private const double CorrectionSeconds = 30.0;

        private readonly int _sampleRate;
        private long _windowSamples;
        private int _windowMinFill = int.MaxValue;
        private volatile int _compensationPpm;

        public AudioRateServo(int sampleRate, int targetSamples)
        {
            _sampleRate = sampleRate;
            TargetSamples = targetSamples;
        }

        /// <summary>The minimum fill to maintain, in samples.</summary>
        public int TargetSamples { get; }

        /// <summary>Correction to apply now; positive produces MORE output samples (raises fill).</summary>
        public int CompensationPpm => _compensationPpm;

        /// <summary>Minimum fill of the last closed window, or -1 before the first one closes.</summary>
        public int ObservedMinSamples { get; private set; } = -1;

        /// <summary>
        /// Reports the fill left after one consumer tick. When the observation window closes,
        /// the correction is recomputed from that window's minimum fill.
        /// </summary>
        public int Update(int fillSamples, int tickSamples)
        {
            if (fillSamples < _windowMinFill)
                _windowMinFill = fillSamples;

            _windowSamples += tickSamples;
            if (_windowSamples < WindowSeconds * _sampleRate)
                return _compensationPpm;

            ObservedMinSamples = _windowMinFill;

            // Work the error off over CorrectionSeconds. A constant clock offset leaves a small
            // residual error (25 ppm × 30 s ≈ 36 samples): the fill settles slightly off target,
            // which is inaudible and harmless.
            double error = _windowMinFill - TargetSamples;
            double ppm = -error * 1_000_000.0 / (CorrectionSeconds * _sampleRate);
            _compensationPpm = (int)Math.Round(Math.Clamp(ppm, -MaxCompensationPpm, MaxCompensationPpm));

            _windowSamples = 0;
            _windowMinFill = int.MaxValue;
            return _compensationPpm;
        }

        /// <summary>Forgets the window and the correction (the buffer is starting over).</summary>
        public void Reset()
        {
            _windowSamples = 0;
            _windowMinFill = int.MaxValue;
            _compensationPpm = 0;
            ObservedMinSamples = -1;
        }
    }
}
