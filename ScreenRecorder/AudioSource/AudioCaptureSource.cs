using System;
using System.Threading;
using MediaEncoder;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace ScreenRecorder.AudioSource
{
    public sealed class AudioCaptureSource : IAudioSource, IDisposable
    {
        private ManualResetEvent _needToStop;

        private Thread _workerThread;

        public AudioCaptureSource()
        {
            _needToStop = new ManualResetEvent(false);
            _workerThread = new Thread(WorkerThreadHandler) { Name = "AudioCaptureSource", IsBackground = true };
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
            WaveInEvent sourceStream = null;
            MMDevice audioCaptureDevice = null;

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
                            if (audioCaptureDevice == null)
                            {
                                audioCaptureDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);

                                sourceStream = new WaveInEvent();
                                sourceStream.WaveFormat = new WaveFormat(48000, 2);
                                sourceStream.DataAvailable += SourceStream_DataAvailable;
                                sourceStream.BufferMilliseconds = 10;
                                sourceStream.StartRecording();
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

                    if (audioCaptureDevice != null)
                    {
                        if (sourceStream != null)
                        {
                            sourceStream.StopRecording();
                            sourceStream.DataAvailable -= SourceStream_DataAvailable;
                            sourceStream.Dispose();
                            sourceStream = null;
                        }

                        audioCaptureDevice.Dispose();
                        audioCaptureDevice = null;
                    }

                    enumerator.UnregisterEndpointNotificationCallback(notificationClient);
                    needToReset?.Dispose();
                }
            }
        }

        private void SourceStream_DataAvailable(object sender, WaveInEventArgs e)
        {
            if ((e?.BytesRecorded ?? 0) > 0)
            {
                var samples = e.BytesRecorded / 4;
                unsafe
                {
                    fixed (void* pBuffer = e.Buffer)
                    {
                        var eventArgs =
                            new NewAudioPacketEventArgs(48000, 2, SampleFormat.S16, samples, new IntPtr(pBuffer));
                        NewAudioPacket?.Invoke(this, eventArgs);
                    }
                }
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
                if (flow == DataFlow.Capture && role == Role.Console)
                {
                    _needToReset?.Set();
                }
            }

            void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
        }
    }
}
