using System;
using System.Linq;
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

        public void Start(string format, string url, VideoCodec videoCodec, int videoBitrate, AudioCodec audioCodec, int audioBitrate, string deviceName, bool drawCursor = true)
        {
            if (base.IsRunning)
                return;

            MonitorInfo monitorInfo = DuplicatorCapture.GetActiveMonitorInfos()?.FirstOrDefault(x => x.DeviceName.Equals(deviceName));
            if (monitorInfo == null)
            {
                throw new ArgumentException($"{deviceName} is not exist");
            }

            screenVideoSource = new ScreenVideoSource(deviceName, drawCursor);
            loopbackAudioSource = new LoopbackAudioSource();
            try
            {
                base.Start(format, url,
                    screenVideoSource, videoCodec, videoBitrate, new VideoSize(monitorInfo.Width, monitorInfo.Height),
                    loopbackAudioSource, audioCodec, audioBitrate);
            }
            catch (Exception ex)
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

            loopbackAudioSource?.Dispose();
            loopbackAudioSource = null;
        }
    }
}
