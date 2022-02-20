using MediaEncoder;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.AudioSource
{
    public class AudioSourceResampler
    {
        private readonly IAudioSource audioSource;
        private bool isDisposed;
        private readonly int outputChannels;
        private readonly int outputSampleRate;
        private readonly SampleFormat outputSampleFormat;

        private Resampler resampler;
        private readonly int samplePerBytes;
        private readonly object SyncObject = new object();

        public AudioSourceResampler(IAudioSource audioSource, int outputChannels, SampleFormat outputSampleFormat,
            int outputSampleRate, int bufferSize = 3200 * 10)
        {
            Buffer = new CircularBuffer(bufferSize);
            this.audioSource = audioSource;
            this.outputChannels = outputChannels;
            this.outputSampleRate = outputSampleRate;
            this.outputSampleFormat = outputSampleFormat;
            switch (outputSampleFormat)
            {
                case SampleFormat.S16:
                    samplePerBytes = 2;
                    break;
            }

            resampler = new Resampler();

            if (this.audioSource != null)
            {
                this.audioSource.NewAudioPacket += AudioSource_NewAudioPacket;
            }
        }

        public CircularBuffer Buffer { get; }

        public bool IsValidBuffer => audioSource != null;

        private void AudioSource_NewAudioPacket(object sender, NewAudioPacketEventArgs eventArgs)
        {
            lock (SyncObject)
            {
                if (isDisposed)
                {
                    return;
                }

                if (eventArgs.Channels != outputChannels || eventArgs.SampleFormat != outputSampleFormat ||
                    eventArgs.SampleRate != outputSampleRate)
                {
                    resampler.Resampling(eventArgs.Channels, eventArgs.SampleFormat, eventArgs.SampleRate,
                        outputChannels, outputSampleFormat, outputSampleRate, eventArgs.DataPointer, eventArgs.Samples,
                        out var destData, out var destSamples);
                    Buffer.Write(destData, 0, destSamples * outputChannels * samplePerBytes);
                }
                else
                {
                    Buffer.Write(eventArgs.DataPointer, 0, eventArgs.Samples * eventArgs.Channels * samplePerBytes);
                }
            }
        }

        public void Dispose()
        {
            lock (SyncObject)
            {
                if (audioSource != null)
                {
                    audioSource.NewAudioPacket -= AudioSource_NewAudioPacket;
                }

                resampler?.Dispose();
                resampler = null;

                isDisposed = true;
            }
        }
    }
}
