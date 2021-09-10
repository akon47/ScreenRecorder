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

        private AudioMixer audioMixer;
        private LoopbackAudioSource loopbackAudioSource;
        private AudioCaptureSource audioCaptureSource;

        public void Start(string format, string url, VideoCodec videoCodec, int videoBitrate, AudioCodec audioCodec, int audioBitrate, string deviceName, Rect region, bool drawCursor, bool recordMicrophone)
        {
            if (base.IsRunning)
                return;

            MonitorInfo monitorInfo = MonitorInfo.GetActiveMonitorInfos()?.FirstOrDefault(x => x.DeviceName.Equals(deviceName));
            if (monitorInfo == null)
            {
                throw new ArgumentException($"{deviceName} is not exist");
            }

            screenVideoSource = new ScreenVideoSource(deviceName, region, drawCursor);

            
            loopbackAudioSource = new LoopbackAudioSource();
            IAudioSource audioSource = loopbackAudioSource;
            if (recordMicrophone)
            {
                audioCaptureSource = new AudioCaptureSource();
                audioMixer = new AudioMixer(loopbackAudioSource, audioCaptureSource);
                audioSource = audioMixer;
            }
            try
            {
                Rect validRegion = Rect.Intersect(region, new Rect(0, 0, monitorInfo.Width, monitorInfo.Height));
                base.Start(format, url,
                    screenVideoSource, videoCodec, videoBitrate, new VideoSize((int)validRegion.Width, (int)validRegion.Height),
                    audioSource, audioCodec, audioBitrate);
            }
            catch (Exception ex)
            {
                base.Stop();

                screenVideoSource?.Dispose();
                screenVideoSource = null;

                audioMixer?.Dispose();
                audioMixer = null;

                loopbackAudioSource?.Dispose();
                loopbackAudioSource = null;

                audioCaptureSource?.Dispose();
                audioCaptureSource = null;
                throw ex;
            }
        }

        protected override void OnEncoderStopped(EncoderStoppedEventArgs args)
        {
            screenVideoSource?.Dispose();
            screenVideoSource = null;

            audioMixer?.Dispose();
            audioMixer = null;

            loopbackAudioSource?.Dispose();
            loopbackAudioSource = null;

            audioCaptureSource?.Dispose();
            audioCaptureSource = null;
        }
    }
}
