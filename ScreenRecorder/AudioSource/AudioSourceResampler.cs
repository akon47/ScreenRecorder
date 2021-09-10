using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediaEncoder;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.AudioSource
{
    public class AudioSourceResampler
    {
        private object SyncObject = new object();

        private IAudioSource audioSource;
        private CircularBuffer circularBuffer;
        public CircularBuffer Buffer
        {
            get
            {
                return circularBuffer;
            }
        }
        private bool isDisposed = false;

        public bool IsValidBuffer
        {
            get => audioSource != null;
        }

        private Resampler resampler;
        private int outputChannels, outputSampleRate;
        private SampleFormat outputSampleFormat;
        private int samplePerBytes;

        public AudioSourceResampler(IAudioSource audioSource, int outputChannels, SampleFormat outputSampleFormat, int outputSampleRate, int bufferSize = 3200 * 10)
        {
            this.circularBuffer = new CircularBuffer(bufferSize);
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
            this.resampler = new Resampler();

            if (this.audioSource != null)
                this.audioSource.NewAudioPacket += AudioSource_NewAudioPacket;
        }

        private void AudioSource_NewAudioPacket(object sender, NewAudioPacketEventArgs eventArgs)
        {
            lock (SyncObject)
            {
                if (isDisposed)
                    return;

                if (eventArgs.Channels != outputChannels || eventArgs.SampleFormat != outputSampleFormat || eventArgs.SampleRate != outputSampleRate)
                {
                    resampler.Resampling(eventArgs.Channels, eventArgs.SampleFormat, eventArgs.SampleRate,
                        outputChannels, outputSampleFormat, outputSampleRate, eventArgs.DataPointer, eventArgs.Samples, out IntPtr destData, out int destSamples);
                    circularBuffer.Write(destData, 0, destSamples * outputChannels * samplePerBytes);
                }
                else
                {
                    circularBuffer.Write(eventArgs.DataPointer, 0, eventArgs.Samples * eventArgs.Channels * samplePerBytes);
                }
            }
        }

        public void Dispose()
        {
            lock (SyncObject)
            {
                if (this.audioSource != null)
                    this.audioSource.NewAudioPacket -= AudioSource_NewAudioPacket;

                resampler?.Dispose();
                resampler = null;

                isDisposed = true;
            }
        }
    }
}
