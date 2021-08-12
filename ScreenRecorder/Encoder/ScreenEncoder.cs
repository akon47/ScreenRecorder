using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediaEncoder;
using ScreenRecorder.DirectX;
using ScreenRecorder.VideoSource;

namespace ScreenRecorder.Encoder
{
	public class ScreenEncoder : Encoder
	{
		private ScreenVideoSource screenVideoSource;

		public void Start(string format, string url, VideoCodec videoCodec, int videoBitrate, AudioCodec audioCodec, int audioBitrate, string deviceName = null, bool drawCursor = true)
		{
			if (base.IsRunning)
				return;

			MonitorInfo monitorInfo = DuplicatorCapture.GetActiveMonitorInfos()?.First(x => x.DeviceName.Equals(deviceName));
			if (monitorInfo == null)
			{
				throw new ArgumentException($"{deviceName} is not exist");
			}

			screenVideoSource = new ScreenVideoSource(deviceName, drawCursor);
			try
			{
				base.Start(format, url, screenVideoSource, videoCodec, videoBitrate, new VideoSize(monitorInfo.Width, monitorInfo.Height), null, AudioCodec.None, 0);
			}
			catch(Exception ex)
			{
				screenVideoSource?.Dispose();
				screenVideoSource = null;
				throw ex;
			}
		}

		protected override void OnEncoderStopped(EncoderStoppedEventArgs args)
		{
			screenVideoSource?.Dispose();
			screenVideoSource = null;
		}
	}
}
