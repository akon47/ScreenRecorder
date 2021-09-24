using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.AudioSource
{
    public class AudioMixer : IAudioSource, IDisposable
    {
        [DllImport("Kernel32.dll", EntryPoint = "RtlZeroMemory", SetLastError = false)]
        internal static extern void ZeroMemory(IntPtr dest, IntPtr size);

        private Thread mixerThread, renderThread;
        private ManualResetEvent needToStop;
        private CircularBuffer circularMixerBuffer;
        private IAudioSource[] audioSources;

        public event NewAudioPacketEventHandler NewAudioPacket;

        public AudioMixer(params IAudioSource[] audioSources)
        {
            this.audioSources = audioSources;
            int framePerBytes = (int)(48000.0d / SystemClockEvent.Framerate * 4);
            circularMixerBuffer = new CircularBuffer(framePerBytes * 6);

            needToStop = new ManualResetEvent(false);

            mixerThread = new Thread(new ThreadStart(MixerThreadHandler)) { Name = "AudioMixer_Mixer", IsBackground = true };
            mixerThread.Start();

            renderThread = new Thread(new ThreadStart(RenderThreadHandler)) { Name = "AudioMixer_Render", IsBackground = true };
            renderThread.Start();
        }

        private void MixerThreadHandler()
        {
            int framePerBytes = (int)(48000.0d / SystemClockEvent.Framerate * 4);

            AudioSourceResampler[] sources = audioSources.Select(source => new AudioSourceResampler(source, 2, MediaEncoder.SampleFormat.S16, 48000)).ToArray();

            IntPtr sample = Marshal.AllocHGlobal(framePerBytes);
            IntPtr mixSample = Marshal.AllocHGlobal(framePerBytes);

            using (SystemClockEvent systemClockEvent = new SystemClockEvent())
            {
                while (!needToStop.WaitOne(0, false))
                {
                    if (systemClockEvent.WaitOne(10))
                    {
                        int count = sources[0].Buffer.Read(mixSample, framePerBytes);
                        if (count < framePerBytes)
                            ZeroMemory(mixSample + count, new IntPtr(framePerBytes - count));

                        for (int i = 1; i < sources.Length; i++)
                        {
                            if (sources[i].IsValidBuffer)
                            {
                                count = sources[i].Buffer.Read(sample, framePerBytes);
                                if (count < framePerBytes)
                                    ZeroMemory(sample + count, new IntPtr(framePerBytes - count));
                                MixStereoSamples(sample, mixSample, mixSample, count / 4);
                            }
                        }
                        circularMixerBuffer.Write(mixSample, 0, framePerBytes);
                    }
                }
            }

            Marshal.FreeHGlobal(sample);
            Marshal.FreeHGlobal(mixSample);

            foreach (var source in sources)
                source.Dispose();
        }

        private void MixStereoSamples(IntPtr sample1, IntPtr sample2, IntPtr mix, int samples = 1)
        {
            unsafe
            {
                short* pSample1 = (short*)sample1.ToPointer();
                short* pSample2 = (short*)sample2.ToPointer();
                short* pMixSample = (short*)mix;
                for (int s = 0; s < samples; s++)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        float s1 = (*pSample1 - 32768) / 32768.0f;
                        float s2 = (*pSample2 - 32768) / 32768.0f;
                        if (Math.Abs(s1 * s2) > 0.25f)
                        {
                            *pMixSample++ = (short)(*pSample1 + *pSample2);
                        }
                        else
                        {
                            *pMixSample++ = Math.Abs(s1) < Math.Abs(s2) ? *pSample1 : *pSample2;
                        }
                        pSample1++;
                        pSample2++;
                    }
                }
            }
        }

        private void RenderThreadHandler()
        {
            int samples = (int)(48000.0d / SystemClockEvent.Framerate);

            IntPtr mixerAudioBuffer = Marshal.AllocHGlobal(samples * 2 * 2); // 16bit 2channels
            using (SystemClockEvent systemClockEvent = new SystemClockEvent())
            {
                while (!needToStop.WaitOne(0, false))
                {
                    if (systemClockEvent.WaitOne(10))
                    {
                        if (circularMixerBuffer.Count >= (samples * 2 * 2))
                        {
                            circularMixerBuffer.Read(mixerAudioBuffer, (samples * 2 * 2));
                            NewAudioPacket?.Invoke(this, new NewAudioPacketEventArgs(48000, 2, MediaEncoder.SampleFormat.S16, samples, mixerAudioBuffer));
                        }
                    }
                }
            }
            Marshal.FreeHGlobal(mixerAudioBuffer);
        }

        public void Dispose()
        {
            if (needToStop != null)
                needToStop.Set();
            if (mixerThread != null)
            {
                if (mixerThread.IsAlive && !mixerThread.Join(500))
                    mixerThread.Abort();
                mixerThread = null;
            }
            if (renderThread != null)
            {
                if (renderThread.IsAlive && !renderThread.Join(500))
                    renderThread.Abort();
                renderThread = null;
            }
            if (needToStop != null)
                needToStop.Close();
            needToStop = null;
        }
    }
}
