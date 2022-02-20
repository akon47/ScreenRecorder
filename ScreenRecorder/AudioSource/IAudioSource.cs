using System;
using MediaEncoder;

namespace ScreenRecorder.AudioSource
{
    public delegate void NewAudioPacketEventHandler(object sender, NewAudioPacketEventArgs eventArgs);

    public interface IAudioSource
    {
        event NewAudioPacketEventHandler NewAudioPacket;
    }

    public class NewAudioPacketEventArgs : EventArgs
    {
        public NewAudioPacketEventArgs(int sampleRate, int channels, SampleFormat sampleFormat, int samples,
            IntPtr dataPointer)
        {
            SampleRate = sampleRate;
            Channels = channels;
            SampleFormat = sampleFormat;
            Samples = samples;
            DataPointer = dataPointer;
        }

        public IntPtr DataPointer { get; }
        public int Samples { get; }
        public int SampleRate { get; }
        public int Channels { get; }
        public SampleFormat SampleFormat { get; }
    }
}
