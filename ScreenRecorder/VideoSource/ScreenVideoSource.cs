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

        private Thread _workerThread;
        private ManualResetEvent _needToStop;
        private AutoResetEvent _needToReset;

        public ScreenVideoSource(string deviceName, Rect region, bool drawCursor)
        {
            _needToReset = new AutoResetEvent(false);
            _needToStop = new ManualResetEvent(false);
            _workerThread = new Thread(new ParameterizedThreadStart(WorkerThreadHandler)) { Name = "ScreenVideoSource", IsBackground = true };
            _workerThread.Start(new ScreenVideoSourceArguments()
            {
                DeviceName = deviceName,
                Region = region,
                DrawCursor = drawCursor,
            });
        }

        private void WorkerThreadHandler(object argument)
        {
            string deviceName = null;
            var drawCursor = true;
            var region = new Rect(0, 0, double.MaxValue, double.MaxValue);

            if (argument is ScreenVideoSourceArguments screenVideoSourceArguments)
            {
                deviceName = screenVideoSourceArguments.DeviceName;
                drawCursor = screenVideoSourceArguments.DrawCursor;
                region = screenVideoSourceArguments.Region;
            }

            using (var videoClockEvent = new VideoClockEvent())
            {
                while (!_needToStop.WaitOne(0, false))
                {
                    try
                    {
                        using (var displayCapture = new DuplicatorCapture(deviceName, region, drawCursor))
                        {
                            while (!_needToStop.WaitOne(0, false))
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
                                        _needToReset?.Set();
                                    }
                                }

                                if (_needToReset.WaitOne(0, false))
                                    break;
                            }
                        }
                    }
                    catch
                    {
                        if (_needToStop.WaitOne(1000, false))
                            break;
                    }
                }
            }
        }

        public void Dispose()
        {
            _needToStop?.Set();
            if (_workerThread != null)
            {
                if (_workerThread.IsAlive && !_workerThread.Join(3000))
                    _workerThread.Abort();
                _workerThread = null;

                _needToStop?.Close();
                _needToStop = null;
            }

            _needToReset?.Close();
            _needToReset = null;
        }
    }
}
