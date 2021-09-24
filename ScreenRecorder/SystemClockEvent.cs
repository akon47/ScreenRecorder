using System;
using System.Diagnostics;
using System.Threading;

namespace ScreenRecorder
{
    public class SystemClockEvent : IDisposable
    {
        private const int MAX_EVENT_COUNT = 8;

        private static readonly object syncObject = new object();
        private static int referenceCount = -1;
        private static readonly AutoResetEvent[] events = new AutoResetEvent[MAX_EVENT_COUNT];
        private static Thread workerThread;
        private static ManualResetEvent needToStop;
        private static int frameRate = 60;
        private static long interval;
        private static long sleepLimit;

        static SystemClockEvent()
        {
            for (int i = 0; i < events.Length; i++)
            {
                events[i] = new AutoResetEvent(false);
            }
        }

        public static int Framerate
        {
            get => frameRate;
            set
            {
                lock (syncObject)
                {
                    if (frameRate != value)
                    {
                        frameRate = value;
                        UpdateInterval();
                    }
                }
            }
        }

        private static void UpdateInterval()
        {
            interval = (long)(Stopwatch.Frequency / (double)frameRate);
            sleepLimit = interval - (Stopwatch.Frequency / 500);
        }

        public static void Start()
        {
            lock (syncObject)
            {
                needToStop = new ManualResetEvent(false);

                workerThread = new Thread(new ThreadStart(WorkerThreadHandler)) { Name = "SystemClockEventThread", IsBackground = false, Priority = ThreadPriority.Highest };
                workerThread.Start();
            }
        }

        public static void Stop()
        {
            try
            {
                lock (syncObject)
                {
                    if (needToStop != null)
                        needToStop.Set();
                    if (workerThread != null)
                    {
                        if (workerThread.IsAlive && !workerThread.Join(100))
                            workerThread.Abort();
                        workerThread = null;
                        if (needToStop != null)
                            needToStop.Close();
                        needToStop = null;
                    }
                }
            }
            catch { }
        }

        private static void WorkerThreadHandler()
        {
            UpdateInterval();

            long prevTick = Stopwatch.GetTimestamp();
            while (!needToStop.WaitOne(0, false))
            {
                long currentTick = Stopwatch.GetTimestamp();
                if (currentTick - prevTick >= interval)
                {
                    for (int i = 0; i < events.Length; i++)
                        events[i].Set();

                    prevTick += interval;
                }
                else if (currentTick - prevTick < sleepLimit)
                {
                    if (needToStop.WaitOne(1, false))
                        break;
                }
            }
        }

        private readonly int currentReferenceIndex;

        public SystemClockEvent()
        {
            currentReferenceIndex = Interlocked.Increment(ref referenceCount);
            if (currentReferenceIndex >= MAX_EVENT_COUNT)
            {
                throw new OutOfMemoryException();
            }
        }

        public bool WaitOne(int millisecondsTimeout = Timeout.Infinite)
        {
            return events[currentReferenceIndex].WaitOne(millisecondsTimeout);
        }

        public bool WaitOne(int millisecondsTimeout, bool exitContext)
        {
            return events[currentReferenceIndex].WaitOne(millisecondsTimeout, exitContext);
        }

        public void Dispose()
        {
            _ = Interlocked.Decrement(ref referenceCount);
        }
    }
}
