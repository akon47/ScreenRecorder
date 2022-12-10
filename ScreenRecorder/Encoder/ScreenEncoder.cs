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
        private ScreenVideoSource _screenVideoSource;

        private AudioMixer _audioMixer;
        private LoopbackAudioSource _loopbackAudioSource;
        private AudioCaptureSource _audioCaptureSource;
        private Utils.ThreadExecutionState? _oldSleepState;

        public ScreenEncoder()
        {
            this.EncoderStopped += ScreenEncoder_EncoderStopped;
        }

        public void Start(string format, string url, VideoCodec videoCodec, int videoBitrate, AudioCodec audioCodec, int audioBitrate, string deviceName, Rect region, bool drawCursor, bool recordMicrophone)
        {
            if (base.IsRunning)
                return;

            // to be on the safe side
            Debug.Assert(_screenVideoSource == null);
            Debug.Assert(_loopbackAudioSource == null);
            Debug.Assert(_audioCaptureSource == null);
            Debug.Assert(_audioMixer == null);
            ScreenEncoder_EncoderStopped(this, null);

            // prevent power saving during capture
            if (!_oldSleepState.HasValue)
                _oldSleepState = Utils.DisableSleep();

            MonitorInfo monitorInfo = MonitorInfo.GetActiveMonitorInfos()?.FirstOrDefault(x => x.DeviceName.Equals(deviceName));
            if (monitorInfo == null)
            {
                throw new ArgumentException($"{deviceName} is not exist");
            }

            _screenVideoSource = new ScreenVideoSource(deviceName, region, drawCursor);


            _loopbackAudioSource = new LoopbackAudioSource();
            IAudioSource audioSource = _loopbackAudioSource;
            if (recordMicrophone)
            {
                _audioCaptureSource = new AudioCaptureSource();
                _audioMixer = new AudioMixer(_loopbackAudioSource, _audioCaptureSource);
                audioSource = _audioMixer;
            }
            try
            {
                Rect validRegion = Rect.Intersect(region, new Rect(0, 0, monitorInfo.Width, monitorInfo.Height));
                base.Start(format, url,
                    _screenVideoSource, videoCodec, videoBitrate, new VideoSize((int)validRegion.Width, (int)validRegion.Height),
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
            _screenVideoSource?.Dispose();
            _screenVideoSource = null;

            _audioMixer?.Dispose();
            _audioMixer = null;

            _loopbackAudioSource?.Dispose();
            _loopbackAudioSource = null;

            _audioCaptureSource?.Dispose();
            _audioCaptureSource = null;

            if (eventArgs != null)
            {
                if (_oldSleepState.HasValue)
                {
                    Utils.SetThreadExecutionState(_oldSleepState.Value);
                    _oldSleepState = null; ;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                base.Dispose(disposing);
                this.EncoderStopped -= ScreenEncoder_EncoderStopped;
                if (_oldSleepState.HasValue)
                {
                    Utils.SetThreadExecutionState(_oldSleepState.Value);
                    _oldSleepState = null;
                }
            }
        }
    }
}
