using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenRecorder.AudioSource
{
    public class AudioCaptureDevice : IAudioSource, IDisposable
    {
        static private Dictionary<string, string> friendlyNameCache = new Dictionary<string, string>();
        static public IEnumerable<string> EnumerateDeviceNames()
        {
            using (MMDeviceEnumerator enumerator = new MMDeviceEnumerator())
            {
                int waveInDevices = WaveInEvent.DeviceCount;
                for (int waveInDevice = 0; waveInDevice < waveInDevices; waveInDevice++)
                {
                    WaveInCapabilities deviceInfo = WaveInEvent.GetCapabilities(waveInDevice);
                    lock (friendlyNameCache)
                    {
                        if (friendlyNameCache.ContainsKey(deviceInfo.ProductName))
                        {
                            yield return friendlyNameCache[deviceInfo.ProductName];
                            continue;
                        }

                        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                        {
                            if (device.State != DeviceState.NotPresent && device.FriendlyName.StartsWith(deviceInfo.ProductName))
                            {
                                friendlyNameCache.Add(deviceInfo.ProductName, device.FriendlyName);
                                yield return device.FriendlyName;
                                break;
                            }
                        }
                    }
                }
            }
        }

        private string selectedAudioCaptureDevice;

        public string SelectedAudioCaptureDevice
        {
            get
            {
                return selectedAudioCaptureDevice;
            }
            set
            {
                lock (syncObject)
                {
                    if (selectedAudioCaptureDevice != value)
                    {
                        selectedAudioCaptureDevice = value;
                    }
                }
            }
        }

        public event NewAudioPacketEventHandler NewAudioPacket;

        private object syncObject = new object();
        private Thread workerThread;
        private ManualResetEvent needToStop;

        public AudioCaptureDevice()
        {
            needToStop = new ManualResetEvent(false);
            workerThread = new Thread(new ThreadStart(WorkerThreadHandler)) { Name = "AudioCaptureDevice", IsBackground = true };
            workerThread.Start();
        }

        private void WorkerThreadHandler()
        {
            while (!needToStop.WaitOne(0, false))
            {
                try
                {
                    Process();
                }
                catch { }

                if (needToStop.WaitOne(3000))
                {
                    break;
                }
            }
        }

        private void Process()
        {
            WaveInEvent sourceStream = null;
            MMDevice audioCaptureDevice = null;
            string audioCaptureFriendlyName = null;

            string[] deviceNames = EnumerateDeviceNames().ToArray();

            while (!needToStop.WaitOne(100, false))
            {
                string targetAudioDeviceName = this.selectedAudioCaptureDevice;

                if (audioCaptureFriendlyName != null && !audioCaptureFriendlyName.Equals(targetAudioDeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    if (sourceStream != null)
                    {
                        sourceStream.StopRecording();
                        sourceStream.DataAvailable -= SourceStream_DataAvailable;
                        sourceStream.Dispose();
                        sourceStream = null;
                    }
                    if (audioCaptureDevice != null)
                    {
                        audioCaptureDevice.Dispose();
                        audioCaptureDevice = null;
                    }
                    audioCaptureFriendlyName = null;
                }

                if (audioCaptureDevice == null)
                {
                    if (deviceNames != null && deviceNames.Contains(targetAudioDeviceName))
                    {
                        using (MMDeviceEnumerator enumerator = new MMDeviceEnumerator())
                        {
                            int waveInDevices = WaveInEvent.DeviceCount;
                            for (int waveInDevice = 0; waveInDevice < waveInDevices && audioCaptureDevice == null; waveInDevice++)
                            {
                                WaveInCapabilities deviceInfo = WaveInEvent.GetCapabilities(waveInDevice);
                                foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                                {
                                    if (device.State != DeviceState.NotPresent && device.FriendlyName.StartsWith(deviceInfo.ProductName) &&
                                        device.FriendlyName.Equals(targetAudioDeviceName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        audioCaptureDevice = device;

                                        audioCaptureFriendlyName = audioCaptureDevice.FriendlyName;

                                        sourceStream = new WaveInEvent();
                                        sourceStream.DeviceNumber = waveInDevice;
                                        sourceStream.WaveFormat = new WaveFormat(48000, 2);
                                        sourceStream.DataAvailable += SourceStream_DataAvailable;
                                        sourceStream.BufferMilliseconds = 10;
                                        sourceStream.StartRecording();
                                        break;
                                    }
                                }
                            }
                        }
                    }
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
        }

        protected virtual void OnNewAudioPacket(NewAudioPacketEventArgs eventArgs)
        {

        }

        private void SourceStream_DataAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                if ((e?.BytesRecorded ?? 0) > 0)
                {
                    unsafe
                    {
                        fixed (byte* pBuffer = e.Buffer)
                        {
                            NewAudioPacketEventArgs eventArgs = new NewAudioPacketEventArgs(48000, 2, MediaEncoder.SampleFormat.S16, e.BytesRecorded / 4, new IntPtr(pBuffer));
                            OnNewAudioPacket(eventArgs);
                            NewAudioPacket?.Invoke(this, eventArgs);
                        }
                    }
                }
            }
            catch { }
        }

        public virtual void Dispose()
        {
            if (needToStop != null)
            {
                needToStop.Set();
            }
            if (workerThread != null)
            {
                if (workerThread.IsAlive && !workerThread.Join(10000))
                    workerThread.Abort();
                workerThread = null;

                if (needToStop != null)
                    needToStop.Close();
                needToStop = null;
            }
        }
    }
}
