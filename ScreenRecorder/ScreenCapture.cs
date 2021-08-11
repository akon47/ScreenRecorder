using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Win32;
using ScreenRecorder.DirectX;
using ScreenRecorder.VideoSource;

namespace ScreenRecorder
{
	public sealed class ScreenCapture : IDisposable
	{
		public event NewVideoFrameEventHandler NewVideoFrame;

		private Thread workerThread;
		private ManualResetEvent needToStop;
		private AutoResetEvent needToReset;

		public ScreenCapture()
		{
			needToReset = new AutoResetEvent(false);
			needToStop = new ManualResetEvent(false);
			workerThread = new Thread(new ThreadStart(WorkerThreadHandler)) { Name = "MonitorCapture", IsBackground = true };
			workerThread.Start();

			SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
		}

		private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
		{
			if (needToReset != null)
				needToReset.Set();
		}

		private void WorkerThreadHandler()
		{
			AppConfig.Instance.WhenChanged(() =>
			{
				if (needToReset != null)
					needToReset.Set();
			},
			nameof(AppConfig.ScreenCaptureMonitor),
			nameof(AppConfig.ScreenCaptureCursorVisible));

			using (SystemClockEvent systemClockEvent = new SystemClockEvent())
			{
				while (!needToStop.WaitOne(0, false))
				{
					MonitorInfo[] monitorInfos = DuplicatorCapture.GetActiveMonitorInfos();
					if (monitorInfos != null)
					{
						if(!monitorInfos.Any(x => x.DeviceName.Equals(AppConfig.Instance.ScreenCaptureMonitor)))
						{
							AppConfig.Instance.ScreenCaptureMonitor = monitorInfos[0].DeviceName;
							if (needToReset != null)
								needToReset.WaitOne(0, false);
						}

						try
						{
							using (DuplicatorCapture displayCapture = new DuplicatorCapture(AppConfig.Instance.ScreenCaptureMonitor, AppConfig.Instance.ScreenCaptureCursorVisible))
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
						catch
						{
							if (needToStop.WaitOne(1000, false))
								break;
						}
					}
					else
					{
						if (needToStop.WaitOne(1000, false))
							break;
					}
				}
			}
		}

		public void Dispose()
		{
			SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;

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
