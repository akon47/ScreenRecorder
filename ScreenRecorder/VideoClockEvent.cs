using System;
using System.Diagnostics;
using System.Threading;

namespace ScreenRecorder
{
    public class VideoClockEvent : IDisposable
    {
        private const int MaxEventCount = 8;

        private static readonly object SyncObject = new object();
        private static int referenceCount = -1;
        private static readonly AutoResetEvent[] Events = new AutoResetEvent[MaxEventCount];
        private static Thread workerThread;
        private static ManualResetEvent needToStop;
        private static int frameRate = 60;
        private static long interval;
        private static long sleepLimit;

        static VideoClockEvent()
        {
            for (int i = 0; i < Events.Length; i++)
            {
                Events[i] = new AutoResetEvent(false);
            }
        }

        public static int Framerate
        {
            get => frameRate;
            set
            {
                lock (SyncObject)
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
            lock (SyncObject)
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
                lock (SyncObject)
                {
                    needToStop?.Set();
                    if (workerThread != null)
                    {
                        if (workerThread.IsAlive && !workerThread.Join(100))
                            workerThread.Abort();
                        workerThread = null;
                        needToStop?.Close();
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
                    foreach (var @event in Events)
                        @event.Set();

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

        public VideoClockEvent()
        {
            currentReferenceIndex = Interlocked.Increment(ref referenceCount);
            if (currentReferenceIndex >= MaxEventCount)
            {
                throw new OutOfMemoryException();
            }
        }

        public bool WaitOne(int millisecondsTimeout = Timeout.Infinite)
        {
            return Events[currentReferenceIndex].WaitOne(millisecondsTimeout);
        }

        public bool WaitOne(int millisecondsTimeout, bool exitContext)
        {
            return Events[currentReferenceIndex].WaitOne(millisecondsTimeout, exitContext);
        }

        public void Dispose()
        {
            _ = Interlocked.Decrement(ref referenceCount);
        }
    }
}
