using System;
using System.Linq;
using System.Windows;
using MediaEncoder;
using ScreenRecorder.DirectX;
using ScreenRecorder.VideoSource;

namespace ScreenRecorder.Encoder
{
    /// <summary>
    /// Encoder for PC screen recording. P0: capture/audio sources are not wired (SharpDX +
    /// NAudio dropped). Start validates the capture target and drives the base state machine so
    /// the UI lifecycle works. Real WGC capture + WASAPI audio + power-state management land in
    /// P2/P3.
    /// </summary>
    public class ScreenEncoder : Encoder
    {
        public ScreenEncoder()
        {
            this.EncoderStopped += ScreenEncoder_EncoderStopped;
        }

        public void Start(string format, string url, VideoCodec videoCodec, int videoBitrate, AudioCodec audioCodec, int audioBitrate, string deviceName, Rect region, bool drawCursor, bool recordMicrophone)
        {
            if (base.IsRunning)
                return;

            MonitorInfo monitorInfo = MonitorInfo.GetActiveMonitorInfos()?.FirstOrDefault(x => x.DeviceName.Equals(deviceName));
            if (monitorInfo == null)
            {
                throw new ArgumentException($"{deviceName} is not exist");
            }

            Rect validRegion = Rect.Intersect(region, new Rect(0, 0, monitorInfo.Width, monitorInfo.Height));
            base.Start(format, url,
                videoCodec, videoBitrate, new VideoSize((int)validRegion.Width, (int)validRegion.Height),
                audioCodec, audioBitrate);
        }

        private void ScreenEncoder_EncoderStopped(object sender, EncoderStoppedEventArgs eventArgs)
        {
            // P0 stub: capture/audio source disposal + power-state restore land in P2/P3.
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                base.Dispose(disposing);
                this.EncoderStopped -= ScreenEncoder_EncoderStopped;
            }
        }
    }
}
