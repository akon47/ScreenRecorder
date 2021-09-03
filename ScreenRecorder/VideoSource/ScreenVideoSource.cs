using System;
using System.Threading;
using ScreenRecorder.DirectX;

namespace ScreenRecorder.VideoSource
{
    public sealed class ScreenVideoSource : IVideoSource, IDisposable
    {
        private class ScreenVideoSourceArguments
        {
            public string DeviceName { get; set; }
            public bool DrawCursor { get; set; }
        }

        public event NewVideoFrameEventHandler NewVideoFrame;

        private Thread workerThread;
        private ManualResetEvent needToStop;
        private AutoResetEvent needToReset;

        public ScreenVideoSource(string deviceName, bool drawCursor)
        {
            needToReset = new AutoResetEvent(false);
            needToStop = new ManualResetEvent(false);
            workerThread = new Thread(new ParameterizedThreadStart(WorkerThreadHandler)) { Name = "ScreenVideoSource", IsBackground = true };
            workerThread.Start(new ScreenVideoSourceArguments()
            {
                DeviceName = deviceName,
                DrawCursor = drawCursor
            });

            //SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        }

        //private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        //{
        //	if (needToReset != null)
        //		needToReset.Set();
        //}

        private void WorkerThreadHandler(object argument)
        {
            string deviceName = null;
            bool drawCursor = true;

            if (argument is ScreenVideoSourceArguments screenVideoSourceArguments)
            {
                deviceName = screenVideoSourceArguments.DeviceName;
                drawCursor = screenVideoSourceArguments.DrawCursor;
            }

            using (SystemClockEvent systemClockEvent = new SystemClockEvent())
            {
                while (!needToStop.WaitOne(0, false))
                {
                    try
                    {
                        using (DuplicatorCapture displayCapture = new DuplicatorCapture(deviceName, drawCursor))
                        {
                            while (!needToStop.WaitOne(0, false))
                            {
                                if (systemClockEvent.WaitOne(1))
                                {
                                    try
                                    {
                                        if (displayCapture.AcquireNextFrame(out IntPtr dataPointer, out int width, out int height, out int stride, out MediaEncoder.PixelFormat pixelFormat))
                                        {
                                            NewVideoFrame?.Invoke(this, new NewVideoFrameEventArgs(width, height, stride, dataPointer, pixelFormat));
                                        }
                                    }
                                    catch
                                    {
                                        needToReset?.Set();
                                    }
                                }

                                if (needToReset.WaitOne(0, false))
                                    break;
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        if (needToStop.WaitOne(1000, false))
                            break;
                    }
                }
            }
        }

        public void Dispose()
        {
            //SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;

            if (needToStop != null)
            {
                needToStop.Set();
            }
            if (workerThread != null)
            {
                if (workerThread.IsAlive && !workerThread.Join(3000))
                    workerThread.Abort();
                workerThread = null;

                if (needToStop != null)
                    needToStop.Close();
                needToStop = null;
            }
            if (needToReset != null)
                needToReset.Close();
            needToReset = null;
        }
    }
}
