using System;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenRecorder.AudioSource
{
    public sealed class AudioCaptureSource : IAudioSource, IDisposable
    {
        private class NotificationClient : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
        {
            private AutoResetEvent needToReset;
            public NotificationClient(ref AutoResetEvent _needToReset)
            {
                needToReset = _needToReset;
            }

            void NAudio.CoreAudioApi.Interfaces.IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
            {
            }

            void NAudio.CoreAudioApi.Interfaces.IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }
            void NAudio.CoreAudioApi.Interfaces.IMMNotificationClient.OnDeviceRemoved(string deviceId)
            {

            }

            void NAudio.CoreAudioApi.Interfaces.IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
            {
                if (flow == DataFlow.Capture && role == Role.Console)
                {
                    needToReset?.Set();
                }
            }
            void NAudio.CoreAudioApi.Interfaces.IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
        }

        public event NewAudioPacketEventHandler NewAudioPacket;

        private Thread workerThread;
        private ManualResetEvent needToStop;

        public AudioCaptureSource()
        {
            needToStop = new ManualResetEvent(false);
            workerThread = new Thread(new ThreadStart(WorkerThreadHandler)) { Name = "AudioCaptureSource", IsBackground = true };
            workerThread.Start();
        }

        private void WorkerThreadHandler()
        {
            WaveInEvent sourceStream = null;
            MMDevice audioCaptureDevice = null;

            while (!needToStop.WaitOne(0, false))
            {
                using (NAudio.CoreAudioApi.MMDeviceEnumerator enumerator = new MMDeviceEnumerator())
                {
                    AutoResetEvent needToReset = new AutoResetEvent(false);
                    NotificationClient notificationClient = new NotificationClient(ref needToReset);
                    enumerator.RegisterEndpointNotificationCallback(notificationClient);

                    while (!needToStop.WaitOne(100, false))
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
                                if (needToStop.WaitOne(100))
                                    break;
                            }
                        }
                        catch
                        {
                            needToStop.WaitOne(500);
                            break;
                        }

                        if (needToReset.WaitOne(0, false) || needToStop.WaitOne(1))
                            break;
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
                int samples = e.BytesRecorded / 4;
                unsafe
                {
                    fixed (void* pBuffer = e.Buffer)
                    {
                        NewAudioPacketEventArgs eventArgs = new NewAudioPacketEventArgs(48000, 2, MediaEncoder.SampleFormat.S16, samples, new IntPtr(pBuffer));
                        NewAudioPacket?.Invoke(this, eventArgs);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (needToStop != null)
            {
                needToStop.Set();
            }
            if (workerThread != null)
            {
                if (workerThread.IsAlive && !workerThread.Join(1000))
                    workerThread.Abort();
                workerThread = null;

                if (needToStop != null)
                    needToStop.Close();
                needToStop = null;
            }
        }
    }
}
