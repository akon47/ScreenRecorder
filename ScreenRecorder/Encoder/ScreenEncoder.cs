using System;
using System.Linq;
using System.Windows;
using System.Diagnostics;
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
        private Utils.ThreadExecutionState? oldSleepState;

        public ScreenEncoder()
        {
            this.EncoderStopped += ScreenEncoder_EncoderStopped;
        }

        public void Start(string format, string url, VideoCodec videoCodec, int videoBitrate, AudioCodec audioCodec, int audioBitrate, string deviceName, Rect region, bool drawCursor, bool recordMicrophone)
        {
            if (base.IsRunning)
                return;

            // to be on the safe side
            Debug.Assert(screenVideoSource == null);
            Debug.Assert(loopbackAudioSource == null);
            Debug.Assert(audioCaptureSource == null);
            Debug.Assert(audioMixer == null);
            ScreenEncoder_EncoderStopped(this, null);

            // prevent power saving during capture
            if (!oldSleepState.HasValue)
                oldSleepState = Utils.DisableSleep();

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
                ScreenEncoder_EncoderStopped(this, null);
                throw ex;
            }
        }

        private void ScreenEncoder_EncoderStopped(object sender, EncoderStoppedEventArgs eventArgs)
        {
            screenVideoSource?.Dispose();
            screenVideoSource = null;

            audioMixer?.Dispose();
            audioMixer = null;

            loopbackAudioSource?.Dispose();
            loopbackAudioSource = null;

            audioCaptureSource?.Dispose();
            audioCaptureSource = null;

            if (eventArgs != null)
            {
                if (oldSleepState.HasValue)
                {
                    Utils.SetThreadExecutionState(oldSleepState.Value);
                    oldSleepState = null; ;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                base.Dispose(disposing);
                this.EncoderStopped -= ScreenEncoder_EncoderStopped;
                if (oldSleepState.HasValue)
                {
                    Utils.SetThreadExecutionState(oldSleepState.Value);
                    oldSleepState = null;
                }
            }
        }
    }
}
