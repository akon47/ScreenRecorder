using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenRecorder
{
	public class SystemClockEvent : IDisposable
	{
		private const int MAX_EVENT_COUNT = 8;

		static private object syncObject = new object();
		static private int referenceCount = -1;
		static private AutoResetEvent[] events = new AutoResetEvent[MAX_EVENT_COUNT];
		static private Thread workerThread;
		static private ManualResetEvent needToStop;

		static SystemClockEvent()
		{
			for (int i = 0; i < events.Length; i++)
				events[i] = new AutoResetEvent(false);
		}

		static public void Start()
		{
			lock (syncObject)
			{
				needToStop = new ManualResetEvent(false);

				workerThread = new Thread(new ThreadStart(WorkerThreadHandler)) { Name = "SystemClockEventThread", IsBackground = false, Priority = ThreadPriority.Highest };
				workerThread.Start();
			}
		}

		static public void Stop()
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

		static void WorkerThreadHandler()
		{
			long interval = (long)(Stopwatch.Frequency / AppConstants.Framerate);
			long sleepLimit = interval - (Stopwatch.Frequency / 500);
			long prevTick = Stopwatch.GetTimestamp();

			while(!needToStop.WaitOne(0, false))
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

		private int currentReferenceIndex = -1;

		public SystemClockEvent()
		{
			currentReferenceIndex = Interlocked.Increment(ref SystemClockEvent.referenceCount);
			if (currentReferenceIndex >= MAX_EVENT_COUNT)
				throw new OutOfMemoryException();
		}

		public bool WaitOne(int millisecondsTimeout = Timeout.Infinite)
		{
			return SystemClockEvent.events[currentReferenceIndex].WaitOne(millisecondsTimeout);
		}

		public bool WaitOne(int millisecondsTimeout, bool exitContext)
		{
			return SystemClockEvent.events[currentReferenceIndex].WaitOne(millisecondsTimeout, exitContext);
		}

		public void Dispose()
		{
			Interlocked.Decrement(ref SystemClockEvent.referenceCount);
		}
	}
}
