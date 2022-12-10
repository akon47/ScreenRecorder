using System;
using System.Runtime.InteropServices;
using System.Threading;
using MediaEncoder;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace ScreenRecorder.AudioSource
{
    public sealed class LoopbackAudioSource : IAudioSource, IDisposable
    {
        private ManualResetEvent _needToStop;
        private int _sampleRate, _channels, _bitsPerSample;

        private Thread _workerThread;

        public LoopbackAudioSource()
        {
            _needToStop = new ManualResetEvent(false);
            _workerThread = new Thread(WorkerThreadHandler) { Name = "LoopbackAudioSource", IsBackground = true };
            _workerThread.Start();
        }

        public event NewAudioPacketEventHandler NewAudioPacket;

        public void Dispose()
        {
            if (_needToStop != null)
            {
                _needToStop.Set();
            }

            if (_workerThread != null)
            {
                if (_workerThread.IsAlive && !_workerThread.Join(1000))
                {
                    _workerThread.Abort();
                }

                _workerThread = null;

                if (_needToStop != null)
                {
                    _needToStop.Close();
                }

                _needToStop = null;
            }
        }

        private void WorkerThreadHandler()
        {
            WasapiLoopbackCapture waveIn = null;

            while (!_needToStop.WaitOne(0, false))
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    var needToReset = new AutoResetEvent(false);
                    var notificationClient = new NotificationClient(ref needToReset);
                    enumerator.RegisterEndpointNotificationCallback(notificationClient);

                    while (!_needToStop.WaitOne(100, false))
                    {
                        try
                        {
                            if (waveIn == null)
                            {
                                waveIn = new WasapiLoopbackCapture();
                                _sampleRate = waveIn.WaveFormat.SampleRate;
                                _channels = waveIn.WaveFormat.Channels;
                                _bitsPerSample = waveIn.WaveFormat.BitsPerSample;
                                waveIn.DataAvailable += WaveIn_DataAvailable;
                                waveIn.StartRecording();
                            }
                            else
                            {
                                if (_needToStop.WaitOne(100))
                                {
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            _needToStop.WaitOne(500);
                            break;
                        }

                        if (needToReset.WaitOne(0, false) || _needToStop.WaitOne(1))
                        {
                            break;
                        }
                    }

                    if (waveIn != null)
                    {
                        try
                        {
                            waveIn.StopRecording();
                            waveIn.Dispose();
                            waveIn.DataAvailable -= WaveIn_DataAvailable;
                            waveIn = null;
                        }
                        catch { }
                    }

                    enumerator.UnregisterEndpointNotificationCallback(notificationClient);
                    needToReset?.Dispose();
                }
            }
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            if ((e?.BytesRecorded ?? 0) > 0)
            {
                var samples = e.BytesRecorded / ((_bitsPerSample + 7) / 8) / _channels;

                var convertedSamples = Marshal.AllocHGlobal(e.BytesRecorded / 2);
                unsafe
                {
                    fixed (void* pBuffer = e.Buffer)
                    {
                        // FLTP to S16 변환 (추후에 오디오 관련 처리를 간편하게 하기 위해..)
                        var src = (float*)pBuffer;
                        var dest = (short*)convertedSamples.ToPointer();
                        for (var i = 0; i < e.BytesRecorded; i += 4)
                        {
                            *dest++ = (short)(*src++ * 32767.0f);
                        }

                        var eventArgs = new NewAudioPacketEventArgs(_sampleRate, _channels, SampleFormat.S16, samples,
                            convertedSamples);
                        NewAudioPacket?.Invoke(this, eventArgs);
                    }
                }

                Marshal.FreeHGlobal(convertedSamples);
            }
        }

        private class NotificationClient : IMMNotificationClient
        {
            private readonly AutoResetEvent _needToReset;

            public NotificationClient(ref AutoResetEvent needToReset)
            {
                this._needToReset = needToReset;
            }

            void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
            {
            }

            void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }

            void IMMNotificationClient.OnDeviceRemoved(string deviceId)
            {
            }

            void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
            {
                if (flow == DataFlow.Render && role == Role.Console)
                {
                    _needToReset?.Set();
                }
            }

            void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
        }
    }
}
