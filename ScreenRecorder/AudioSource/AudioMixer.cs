using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using MediaEncoder;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.AudioSource
{
    /// <summary>
    /// Audio Mixer (2Ch, 16bit, 48000Hz)
    /// </summary>
    public class AudioMixer : IAudioSource, IDisposable
    {
        #region Fields

        private readonly IAudioSource[] _audioSources;
        private readonly CircularBuffer _circularMixerBuffer;
        private readonly int _samplesPerFrame;
        private readonly int _samplesBytesPerFrame;
        private readonly int _framesPerAdditinalSample;

        private Thread _mixerThread, _renderThread;
        private ManualResetEvent _needToStop;

        #endregion

        #region Constructors

        public AudioMixer(params IAudioSource[] audioSources)
        {
            _audioSources = audioSources;
            _samplesPerFrame = (int)(48000.0d / VideoClockEvent.Framerate);
            _samplesBytesPerFrame = _samplesPerFrame * 2 * 2; // 2Ch, 16bit

            var remainingSamples = 48000 - (_samplesPerFrame * VideoClockEvent.Framerate);
            _framesPerAdditinalSample = remainingSamples != 0 ? VideoClockEvent.Framerate / remainingSamples : 0;

            _circularMixerBuffer = new CircularBuffer(_samplesBytesPerFrame * 6);

            _needToStop = new ManualResetEvent(false);

            _mixerThread = new Thread(MixerThreadHandler) { Name = "AudioMixer_Mixer", IsBackground = true };
            _mixerThread.Start();

            _renderThread = new Thread(RenderThreadHandler) { Name = "AudioMixer_Render", IsBackground = true };
            _renderThread.Start();
        }

        #endregion

        #region Helpers

        [DllImport("Kernel32.dll", EntryPoint = "RtlZeroMemory", SetLastError = false)]
        internal static extern void ZeroMemory(IntPtr dest, IntPtr size);

        public void Dispose()
        {
            if (_needToStop != null)
            {
                _needToStop.Set();
            }

            if (_mixerThread != null)
            {
                if (_mixerThread.IsAlive && !_mixerThread.Join(500))
                {
                    _mixerThread.Abort();
                }

                _mixerThread = null;
            }

            if (_renderThread != null)
            {
                if (_renderThread.IsAlive && !_renderThread.Join(500))
                {
                    _renderThread.Abort();
                }

                _renderThread = null;
            }

            if (_needToStop != null)
            {
                _needToStop.Close();
            }

            _needToStop = null;
        }

        private void MixerThreadHandler()
        {
            var sources = _audioSources.Select(source => new AudioSourceResampler(source, 2, SampleFormat.S16, 48000))
                .ToArray();

            var sample = Marshal.AllocHGlobal(_samplesBytesPerFrame + 4);
            var mixSample = Marshal.AllocHGlobal(_samplesBytesPerFrame + 4);

            using (var systemClockEvent = new VideoClockEvent())
            {
                long frames = 0;
                while (!_needToStop.WaitOne(0, false))
                {
                    if (systemClockEvent.WaitOne(10))
                    {
                        var samplesBytesPerFrame = _samplesBytesPerFrame + (frames++ % 3 == 0 ? 4 : 0);

                        var count = sources[0].Buffer.Read(mixSample, samplesBytesPerFrame);
                        if (count < samplesBytesPerFrame)
                        {
                            ZeroMemory(mixSample + count, new IntPtr(samplesBytesPerFrame - count));
                        }

                        for (var i = 1; i < sources.Length; i++)
                        {
                            if (sources[i].IsValidBuffer)
                            {
                                count = sources[i].Buffer.Read(sample, samplesBytesPerFrame);
                                if (count < samplesBytesPerFrame)
                                {
                                    ZeroMemory(sample + count, new IntPtr(samplesBytesPerFrame - count));
                                }

                                MixStereoSamples(sample, mixSample, mixSample, count / 4);
                            }
                        }

                        _circularMixerBuffer.Write(mixSample, 0, samplesBytesPerFrame);
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
            var mixerAudioBuffer = Marshal.AllocHGlobal(_samplesBytesPerFrame + 4); // 16bit 2channels
            using (var systemClockEvent = new VideoClockEvent())
            {
                long frames = 0;
                while (!_needToStop.WaitOne(0, false))
                {
                    if (systemClockEvent.WaitOne(10))
                    {
                        var samplesBytesPerFrame = _samplesBytesPerFrame + (frames++ % 3 == 0 ? 4 : 0);

                        if (_circularMixerBuffer.Count >= samplesBytesPerFrame)
                        {
                            _circularMixerBuffer.Read(mixerAudioBuffer, samplesBytesPerFrame);
                            OnNewAudioPacket(new NewAudioPacketEventArgs(48000, 2, SampleFormat.S16, samplesBytesPerFrame / 4, mixerAudioBuffer));
                        }
                    }
                }
            }

            Marshal.FreeHGlobal(mixerAudioBuffer);
        }

        #endregion

        #region Events

        public event NewAudioPacketEventHandler NewAudioPacket;

        public void OnNewAudioPacket(NewAudioPacketEventArgs args)
        {
            NewAudioPacket?.Invoke(this, args);
        }

        #endregion
    }
}
