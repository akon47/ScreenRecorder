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
        private ManualResetEvent needToStop;
        private int sampleRate, channels, bitsPerSample;

        private Thread workerThread;

        public LoopbackAudioSource()
        {
            needToStop = new ManualResetEvent(false);
            workerThread = new Thread(WorkerThreadHandler) { Name = "LoopbackAudioSource", IsBackground = true };
            workerThread.Start();
        }

        public event NewAudioPacketEventHandler NewAudioPacket;

        public void Dispose()
        {
            if (needToStop != null)
            {
                needToStop.Set();
            }

            if (workerThread != null)
            {
                if (workerThread.IsAlive && !workerThread.Join(1000))
                {
                    workerThread.Abort();
                }

                workerThread = null;

                if (needToStop != null)
                {
                    needToStop.Close();
                }

                needToStop = null;
            }
        }

        private void WorkerThreadHandler()
        {
            WasapiLoopbackCapture waveIn = null;

            while (!needToStop.WaitOne(0, false))
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    var needToReset = new AutoResetEvent(false);
                    var notificationClient = new NotificationClient(ref needToReset);
                    enumerator.RegisterEndpointNotificationCallback(notificationClient);

                    while (!needToStop.WaitOne(100, false))
                    {
                        try
                        {
                            if (waveIn == null)
                            {
                                waveIn = new WasapiLoopbackCapture();
                                sampleRate = waveIn.WaveFormat.SampleRate;
                                channels = waveIn.WaveFormat.Channels;
                                bitsPerSample = waveIn.WaveFormat.BitsPerSample;
                                waveIn.DataAvailable += WaveIn_DataAvailable;
                                waveIn.StartRecording();
                            }
                            else
                            {
                                if (needToStop.WaitOne(100))
                                {
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            needToStop.WaitOne(500);
                            break;
                        }

                        if (needToReset.WaitOne(0, false) || needToStop.WaitOne(1))
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
                var samples = e.BytesRecorded / ((bitsPerSample + 7) / 8) / channels;

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

                        var eventArgs = new NewAudioPacketEventArgs(sampleRate, channels, SampleFormat.S16, samples,
                            convertedSamples);
                        NewAudioPacket?.Invoke(this, eventArgs);
                    }
                }

                Marshal.FreeHGlobal(convertedSamples);
            }
        }

        private class NotificationClient : IMMNotificationClient
        {
            private readonly AutoResetEvent needToReset;

            public NotificationClient(ref AutoResetEvent _needToReset)
            {
                needToReset = _needToReset;
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
                    needToReset?.Set();
                }
            }

            void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
        }
    }
}
