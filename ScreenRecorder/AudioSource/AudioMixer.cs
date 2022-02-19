using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using MediaEncoder;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.AudioSource
{
    public class AudioMixer : IAudioSource, IDisposable
    {
        private readonly IAudioSource[] audioSources;
        private readonly CircularBuffer circularMixerBuffer;

        private Thread mixerThread, renderThread;
        private ManualResetEvent needToStop;

        public AudioMixer(params IAudioSource[] audioSources)
        {
            this.audioSources = audioSources;
            var framePerBytes = (int)(48000.0d / SystemClockEvent.Framerate * 4);
            circularMixerBuffer = new CircularBuffer(framePerBytes * 6);

            needToStop = new ManualResetEvent(false);

            mixerThread = new Thread(MixerThreadHandler) { Name = "AudioMixer_Mixer", IsBackground = true };
            mixerThread.Start();

            renderThread = new Thread(RenderThreadHandler) { Name = "AudioMixer_Render", IsBackground = true };
            renderThread.Start();
        }

        public event NewAudioPacketEventHandler NewAudioPacket;

        public void Dispose()
        {
            if (needToStop != null)
            {
                needToStop.Set();
            }

            if (mixerThread != null)
            {
                if (mixerThread.IsAlive && !mixerThread.Join(500))
                {
                    mixerThread.Abort();
                }

                mixerThread = null;
            }

            if (renderThread != null)
            {
                if (renderThread.IsAlive && !renderThread.Join(500))
                {
                    renderThread.Abort();
                }

                renderThread = null;
            }

            if (needToStop != null)
            {
                needToStop.Close();
            }

            needToStop = null;
        }

        [DllImport("Kernel32.dll", EntryPoint = "RtlZeroMemory", SetLastError = false)]
        internal static extern void ZeroMemory(IntPtr dest, IntPtr size);

        private void MixerThreadHandler()
        {
            var framePerBytes = (int)(48000.0d / SystemClockEvent.Framerate * 4);

            var sources = audioSources.Select(source => new AudioSourceResampler(source, 2, SampleFormat.S16, 48000))
                .ToArray();

            var sample = Marshal.AllocHGlobal(framePerBytes);
            var mixSample = Marshal.AllocHGlobal(framePerBytes);

            using (var systemClockEvent = new SystemClockEvent())
            {
                while (!needToStop.WaitOne(0, false))
                {
                    if (systemClockEvent.WaitOne(10))
                    {
                        var count = sources[0].Buffer.Read(mixSample, framePerBytes);
                        if (count < framePerBytes)
                        {
                            ZeroMemory(mixSample + count, new IntPtr(framePerBytes - count));
                        }

                        for (var i = 1; i < sources.Length; i++)
                        {
                            if (sources[i].IsValidBuffer)
                            {
                                count = sources[i].Buffer.Read(sample, framePerBytes);
                                if (count < framePerBytes)
                                {
                                    ZeroMemory(sample + count, new IntPtr(framePerBytes - count));
                                }

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
            {
                source.Dispose();
            }
        }

        private void MixStereoSamples(IntPtr sample1, IntPtr sample2, IntPtr mix, int samples = 1)
        {
            unsafe
            {
                var pSample1 = (short*)sample1.ToPointer();
                var pSample2 = (short*)sample2.ToPointer();
                var pMixSample = (short*)mix;
                for (var s = 0; s < samples; s++)
                {
                    for (var i = 0; i < 2; i++)
                    {
                        var s1 = (*pSample1 - 32768) / 32768.0f;
                        var s2 = (*pSample2 - 32768) / 32768.0f;
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
            var samples = (int)(48000.0d / SystemClockEvent.Framerate);

            var mixerAudioBuffer = Marshal.AllocHGlobal(samples * 2 * 2); // 16bit 2channels
            using (var systemClockEvent = new SystemClockEvent())
            {
                while (!needToStop.WaitOne(0, false))
                {
                    if (systemClockEvent.WaitOne(10))
                    {
                        if (circularMixerBuffer.Count >= samples * 2 * 2)
                        {
                            circularMixerBuffer.Read(mixerAudioBuffer, samples * 2 * 2);
                            NewAudioPacket?.Invoke(this,
                                new NewAudioPacketEventArgs(48000, 2, SampleFormat.S16, samples, mixerAudioBuffer));
                        }
                    }
                }
            }

            Marshal.FreeHGlobal(mixerAudioBuffer);
        }
    }
}
