using System;
using System.Linq;
using System.Windows;
using MediaEncoder;
using ScreenRecorder.AudioSource;
using ScreenRecorder.DirectX;
using ScreenRecorder.VideoSource;

namespace ScreenRecorder.Encoder
{
    /// <summary>
    /// Encoder for PC screen recording
    /// </summary>
    public class ScreenEncoder : Encoder
    {
        private ScreenVideoSource screenVideoSource;
        private LoopbackAudioSource loopbackAudioSource;

        public void Start(string format, string url, VideoCodec videoCodec, int videoBitrate, AudioCodec audioCodec, int audioBitrate, string deviceName, Rect region, bool drawCursor = true)
        {
            if (base.IsRunning)
                return;

            MonitorInfo monitorInfo = DuplicatorCapture.GetActiveMonitorInfos()?.FirstOrDefault(x => x.DeviceName.Equals(deviceName));
            if (monitorInfo == null)
            {
                throw new ArgumentException($"{deviceName} is not exist");
            }

            screenVideoSource = new ScreenVideoSource(deviceName, region, drawCursor);
            loopbackAudioSource = new LoopbackAudioSource();
            try
            {
                Rect validRegion = Rect.Intersect(region, new Rect(0, 0, monitorInfo.Width, monitorInfo.Height));
                base.Start(format, url,
                    screenVideoSource, videoCodec, videoBitrate, new VideoSize((int)validRegion.Width, (int)validRegion.Height),
                    loopbackAudioSource, audioCodec, audioBitrate);
            }
            catch (Exception ex)
            {
                base.Stop();

                screenVideoSource?.Dispose();
                screenVideoSource = null;
                throw ex;
            }
        }

        protected override void OnEncoderStopped(EncoderStoppedEventArgs args)
        {
            screenVideoSource?.Dispose();
            screenVideoSource = null;

            loopbackAudioSource?.Dispose();
            loopbackAudioSource = null;
        }
    }
}
