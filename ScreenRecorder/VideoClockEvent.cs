using System;
using System.Diagnostics;
using System.Threading;

namespace ScreenRecorder
{
    public class VideoClockEvent : IDisposable
    {
        private const int MaxEventCount = 8;

        private static readonly object SyncObject = new object();
        private static int _referenceCount = -1;
        private static readonly AutoResetEvent[] Events = new AutoResetEvent[MaxEventCount];
        private static Thread _workerThread;
        private static ManualResetEvent _needToStop;
        private static int _frameRate = 60;
        private static long _interval;
        private static long _sleepLimit;

        static VideoClockEvent()
        {
            for (int i = 0; i < Events.Length; i++)
            {
                Events[i] = new AutoResetEvent(false);
            }
        }

        public static int Framerate
        {
            get => _frameRate;
            set
            {
                lock (SyncObject)
                {
                    if (_frameRate != value)
                    {
                        _frameRate = value;
                        UpdateInterval();
                    }
                }
            }
        }

        private static void UpdateInterval()
        {
            _interval = (long)(Stopwatch.Frequency / (double)_frameRate);
            _sleepLimit = _interval - (Stopwatch.Frequency / 500);
        }

        public static void Start()
        {
            lock (SyncObject)
            {
                _needToStop = new ManualResetEvent(false);

                _workerThread = new Thread(new ThreadStart(WorkerThreadHandler)) { Name = "SystemClockEventThread", IsBackground = false, Priority = ThreadPriority.Highest };
                _workerThread.Start();
            }
        }

        public static void Stop()
        {
            try
            {
                lock (SyncObject)
                {
                    _needToStop?.Set();
                    if (_workerThread != null)
                    {
                        if (_workerThread.IsAlive && !_workerThread.Join(100))
                            _workerThread.Abort();
                        _workerThread = null;
                        _needToStop?.Close();
                        _needToStop = null;
                    }
                }
            }
            catch { }
        }

        private static void WorkerThreadHandler()
        {
            UpdateInterval();

            long prevTick = Stopwatch.GetTimestamp();
            while (!_needToStop.WaitOne(0, false))
            {
                long currentTick = Stopwatch.GetTimestamp();
                if (currentTick - prevTick >= _interval)
                {
                    foreach (var @event in Events)
                        @event.Set();

                    prevTick += _interval;
                }
                else if (currentTick - prevTick < _sleepLimit)
                {
                    if (_needToStop.WaitOne(1, false))
                        break;
                }
            }
        }

        private readonly int _currentReferenceIndex;

        public VideoClockEvent()
        {
            _currentReferenceIndex = Interlocked.Increment(ref _referenceCount);
            if (_currentReferenceIndex >= MaxEventCount)
            {
                throw new OutOfMemoryException();
            }
        }

        public bool WaitOne(int millisecondsTimeout = Timeout.Infinite)
        {
            return Events[_currentReferenceIndex].WaitOne(millisecondsTimeout);
        }

        public bool WaitOne(int millisecondsTimeout, bool exitContext)
        {
            return Events[_currentReferenceIndex].WaitOne(millisecondsTimeout, exitContext);
        }

        public void Dispose()
        {
            _ = Interlocked.Decrement(ref _referenceCount);
        }
    }
}
