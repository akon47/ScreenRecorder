using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
		public IntPtr DataPointer { get; private set; }
		public int Samples { get; private set; }
		public int SampleRate { get; private set; }
		public int Channels { get; private set; }
		public SampleFormat SampleFormat { get; private set; }

		public NewAudioPacketEventArgs(int sampleRate, int channels, SampleFormat sampleFormat, int samples, IntPtr dataPointer)
		{
			this.SampleRate = sampleRate;
			this.Channels = channels;
			this.SampleFormat = sampleFormat;
			this.Samples = samples;
			this.DataPointer = dataPointer;
		}
	}
}
