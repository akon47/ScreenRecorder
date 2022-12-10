using MediaEncoder;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.AudioSource
{
    public class AudioSourceResampler
    {
        private readonly IAudioSource _audioSource;
        private bool _isDisposed;
        private readonly int _outputChannels;
        private readonly int _outputSampleRate;
        private readonly SampleFormat _outputSampleFormat;

        private Resampler _resampler;
        private readonly int _samplePerBytes;
        private readonly object _syncObject = new object();

        public AudioSourceResampler(IAudioSource audioSource, int outputChannels, SampleFormat outputSampleFormat,
            int outputSampleRate, int bufferSize = 3200 * 10)
        {
            Buffer = new CircularBuffer(bufferSize);
            this._audioSource = audioSource;
            this._outputChannels = outputChannels;
            this._outputSampleRate = outputSampleRate;
            this._outputSampleFormat = outputSampleFormat;
            switch (outputSampleFormat)
            {
                case SampleFormat.S16:
                    _samplePerBytes = 2;
                    break;
            }

            _resampler = new Resampler();

            if (this._audioSource != null)
            {
                this._audioSource.NewAudioPacket += AudioSource_NewAudioPacket;
            }
        }

        public CircularBuffer Buffer { get; }

        public bool IsValidBuffer => _audioSource != null;

        private void AudioSource_NewAudioPacket(object sender, NewAudioPacketEventArgs eventArgs)
        {
            lock (_syncObject)
            {
                if (_isDisposed)
                {
                    return;
                }

                if (eventArgs.Channels != _outputChannels || eventArgs.SampleFormat != _outputSampleFormat ||
                    eventArgs.SampleRate != _outputSampleRate)
                {
                    _resampler.Resampling(eventArgs.Channels, eventArgs.SampleFormat, eventArgs.SampleRate,
                        _outputChannels, _outputSampleFormat, _outputSampleRate, eventArgs.DataPointer, eventArgs.Samples,
                        out var destData, out var destSamples);
                    Buffer.Write(destData, 0, destSamples * _outputChannels * _samplePerBytes);
                }
                else
                {
                    Buffer.Write(eventArgs.DataPointer, 0, eventArgs.Samples * eventArgs.Channels * _samplePerBytes);
                }
            }
        }

        public void Dispose()
        {
            lock (_syncObject)
            {
                if (_audioSource != null)
                {
                    _audioSource.NewAudioPacket -= AudioSource_NewAudioPacket;
                }

                _resampler?.Dispose();
                _resampler = null;

                _isDisposed = true;
            }
        }
    }
}
