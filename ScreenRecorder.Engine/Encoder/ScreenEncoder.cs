using System;
using System.Linq;
using System.Threading;
using System.Windows;
using MediaEncoder;
using ScreenRecorder.AudioSource;
using ScreenRecorder.DirectX;
using ScreenRecorder.VideoSource;

namespace ScreenRecorder.Encoder
{
    /// <summary>
    /// Screen recording encoder. Owns the real pipeline: a single QPC t0 shared by a WGC
    /// <see cref="ScreenVideoSource"/> (P2) and a WASAPI <see cref="WasapiAudioSource"/> (P3),
    /// both feeding one <see cref="Recorder"/> (P1, FFmpeg.AutoGen). The base class drives the
    /// bindable state machine the WPF shell binds to.
    /// </summary>
    public class ScreenEncoder : Encoder
    {
        private volatile Recorder _recorder;
        private ScreenVideoSource _video;
        private WasapiAudioSource _audio;
        private bool _sleepPrevented;

        public ScreenEncoder()
        {
            this.EncoderStopped += ScreenEncoder_EncoderStopped;
        }

        /// <summary>
        /// Live count of video frames recorded so far, read straight from the recorder (lock-free).
        /// The UI samples this once per render frame for a smooth elapsed-time display — there is no
        /// throttling poll in between. Returns 0 when not recording.
        /// </summary>
        public ulong LiveVideoFrames
        {
            get
            {
                var recorder = _recorder;
                return recorder != null ? (ulong)recorder.RecordedVideoFrames : 0;
            }
        }

        public void Start(string format, string url, VideoCodec videoCodec, int videoBitrate, AudioCodec audioCodec, int audioBitrate, string deviceName, Rect region, bool drawCursor, bool recordMicrophone)
        {
            if (base.IsRunning)
                return;

            MonitorInfo monitorInfo = MonitorInfo.GetActiveMonitorInfos()?.FirstOrDefault(x => x.DeviceName.Equals(deviceName));
            if (monitorInfo == null)
                throw new ArgumentException($"{deviceName} is not exist");

            Rect validRegion = Rect.Intersect(region, new Rect(0, 0, monitorInfo.Width, monitorInfo.Height));
            // The shell runs DPI-unaware, so Screen-derived regions are DPI-virtualized while the
            // WGC capture surface is physical pixels — map before sizing/cropping (issue #58).
            validRegion = monitorInfo.VirtualToPhysical(validRegion);
            int width = Math.Max(2, (int)validRegion.Width & ~1);
            int height = Math.Max(2, (int)validRegion.Height & ~1);
            int fps = Math.Max(1, FrameRateProvider.Framerate);

            var videoParams = new VideoParams
            {
                Codec = videoCodec,
                Hw = SelectHwAccel(videoCodec),
                Width = width,
                Height = height,
                FpsNumerator = fps,
                FpsDenominator = 1,
                Bitrate = videoBitrate,
                RateControl = RateControl.Cbr,
            };

            AudioParams audioParams = audioCodec == AudioCodec.None ? null : new AudioParams
            {
                Codec = audioCodec,
                SampleRate = 48000,
                Channels = 2,
                Bitrate = audioBitrate,
                Format = SampleFormat.S16,
            };

            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

            try
            {
                _recorder = new Recorder(url, format, videoParams, audioParams);
                _recorder.Start();

                _video = new ScreenVideoSource(deviceName, validRegion, drawCursor, fps, 1, t0, _recorder);
                if (audioParams != null)
                    _audio = new WasapiAudioSource(loopbackDeviceId: null, micDeviceId: recordMicrophone ? "" : null, t0, _recorder);

                PowerHelper.PreventSleep();
                _sleepPrevented = true;

                // base.Start sets Url + Status=Start and fires EncoderFirstStarting, which lets the
                // shell exclude its own window from capture BEFORE capture begins.
                base.Start(format, url, videoCodec, videoBitrate, new VideoSize(width, height), audioCodec, audioBitrate);

                _video.Start();
                _audio?.Start();
            }
            catch
            {
                base.Stop(); // fires EncoderStopped → cleanup
                throw;
            }
        }

        private static HwAccel SelectHwAccel(VideoCodec codec)
        {
            if (codec == VideoCodec.H264)
                return MediaWriter.IsSupportedNvencH264() ? HwAccel.Nvenc
                    : MediaWriter.IsSupportedQsvH264() ? HwAccel.Qsv : HwAccel.Software;

            return MediaWriter.IsSupportedNvencHEVC() ? HwAccel.Nvenc
                : MediaWriter.IsSupportedQsvHEVC() ? HwAccel.Qsv : HwAccel.Software;
        }

        public override void Pause()
        {
            if (Status == EncoderStatus.Stop)
                return;

            base.Pause();
            _video?.Pause();
            _audio?.Pause();
        }

        public override void Resume()
        {
            if (Status == EncoderStatus.Stop)
                return;

            base.Resume();
            _video?.Resume();
            _audio?.Resume();
        }

        private void ScreenEncoder_EncoderStopped(object sender, EncoderStoppedEventArgs eventArgs)
        {
            _audio?.Stop();
            _audio?.Dispose();
            _audio = null;

            _video?.Stop();
            _video?.Dispose();
            _video = null;

            _recorder?.Stop();   // drains encoders + mux, finalizes the file
            _recorder?.Dispose();
            _recorder = null;

            if (_sleepPrevented)
            {
                PowerHelper.RestoreSleep();
                _sleepPrevented = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                base.Dispose(disposing); // calls Stop() → EncoderStopped → cleanup
                this.EncoderStopped -= ScreenEncoder_EncoderStopped;
            }
        }
    }
}
