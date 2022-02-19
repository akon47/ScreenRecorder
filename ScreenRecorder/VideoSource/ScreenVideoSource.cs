using System;
using System.Threading;
using System.Windows;
using ScreenRecorder.DirectX;

namespace ScreenRecorder.VideoSource
{
    public sealed class ScreenVideoSource : IVideoSource, IDisposable
    {
        private class ScreenVideoSourceArguments
        {
            public string DeviceName { get; set; }
            public Rect Region { get; set; }
            public bool DrawCursor { get; set; }
        }

        public event NewVideoFrameEventHandler NewVideoFrame;

        private Thread workerThread;
        private ManualResetEvent needToStop;
        private AutoResetEvent needToReset;

        public ScreenVideoSource(string deviceName, Rect region, bool drawCursor)
        {
            needToReset = new AutoResetEvent(false);
            needToStop = new ManualResetEvent(false);
            workerThread = new Thread(new ParameterizedThreadStart(WorkerThreadHandler)) { Name = "ScreenVideoSource", IsBackground = true };
            workerThread.Start(new ScreenVideoSourceArguments()
            {
                DeviceName = deviceName,
                Region = region,
                DrawCursor = drawCursor,
            });
        }

        private void WorkerThreadHandler(object argument)
        {
            string deviceName = null;
            bool drawCursor = true;
            Rect region = new Rect(0, 0, double.MaxValue, double.MaxValue);

            if (argument is ScreenVideoSourceArguments screenVideoSourceArguments)
            {
                deviceName = screenVideoSourceArguments.DeviceName;
                drawCursor = screenVideoSourceArguments.DrawCursor;
                region = screenVideoSourceArguments.Region;
            }

            using (VideoClockEvent videoClockEvent = new VideoClockEvent())
            {
                while (!needToStop.WaitOne(0, false))
                {
                    try
                    {
                        using (DuplicatorCapture displayCapture = new DuplicatorCapture(deviceName, region, drawCursor))
                        {
                            while (!needToStop.WaitOne(0, false))
                            {
                                if (videoClockEvent.WaitOne(1))
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
                    catch
                    {
                        if (needToStop.WaitOne(1000, false))
                            break;
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
