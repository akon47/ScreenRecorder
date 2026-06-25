using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaEncoder;
using ScreenRecorder.VideoSource;

namespace ScreenRecorder.Encoder
{
    public delegate void EncoderStoppedEventHandler(object sender, EncoderStoppedEventArgs eventArgs);

    public class EncoderStoppedEventArgs : EventArgs
    {
        public ulong VideoFramesCount { get; }
        public ulong AudioSamplesCount { get; }
        public string Url { get; }

        public EncoderStoppedEventArgs(ulong videoFramesCount, ulong audioSamplesCount, string url)
        {
            VideoFramesCount = videoFramesCount;
            AudioSamplesCount = audioSamplesCount;
            Url = url;
        }
    }

    /// <summary>
    /// Base recorder facade. P0: the legacy capture/encode/mux worker (MediaBuffer + C++/CLI
    /// MediaWriter + VideoClockEvent pulse) is intentionally gone — it is replaced in P1 by the
    /// FFmpeg.AutoGen pipeline and the new QPC ideal-grid CFR timing model (see CLAUDE.md).
    /// Start/Pause/Resume/Stop here only drive the bindable state machine the WPF shell binds to.
    /// </summary>
    public class Encoder : ObservableObject, IDisposable
    {
        #region Bindable Properties

        private ulong _videoFramesCount;
        public ulong VideoFramesCount
        {
            get => _videoFramesCount;
            protected set
            {
                SetProperty(ref _videoFramesCount, value);
                VideoTime = (ulong)(value / (double)Math.Max(1, FrameRateProvider.Framerate));
            }
        }

        private ulong _videoTime;
        public ulong VideoTime
        {
            get => _videoTime;
            private set => SetProperty(ref _videoTime, value);
        }

        private ulong _audioSamplesCount;
        public ulong AudioSamplesCount
        {
            get => _audioSamplesCount;
            protected set => SetProperty(ref _audioSamplesCount, value);
        }

        private string _url;
        public string Url
        {
            get => _url;
            private set => SetProperty(ref _url, value);
        }

        private bool _isStarted;
        public bool IsStarted
        {
            get => _isStarted;
            private set => SetProperty(ref _isStarted, value);
        }

        private bool _isPaused;
        public bool IsPaused
        {
            get => _isPaused;
            private set => SetProperty(ref _isPaused, value);
        }

        private bool _isStopped = true;
        public bool IsStopped
        {
            get => _isStopped;
            private set => SetProperty(ref _isStopped, value);
        }

        private EncoderStatus _status = EncoderStatus.Stop;
        public EncoderStatus Status
        {
            get => _status;
            private set
            {
                if (SetProperty(ref _status, value))
                {
                    IsStarted = (value == EncoderStatus.Start);
                    IsPaused = (value == EncoderStatus.Pause);
                    IsStopped = (value == EncoderStatus.Stop);
                }
            }
        }

        public bool IsRunning => _status != EncoderStatus.Stop;

        private ulong _maximumVideoFramesCount;
        public ulong MaximumVideoFramesCount
        {
            get => _maximumVideoFramesCount;
            set => SetProperty(ref _maximumVideoFramesCount, value);
        }

        #endregion

        #region Lifecycle

        public void Start(string format, string url, VideoCodec videoCodec, int videoBitrate, VideoSize videoSize, AudioCodec audioCodec, int audioBitrate)
        {
            if (IsRunning)
                return;

            Url = url;
            Status = EncoderStatus.Start;
            OnEncoderFirstStarting();

            // P0/P3 stub: the facade only drives the bindable state machine. P4 wires it to the
            // real Recorder + ScreenVideoSource (P2) + WasapiAudioSource (P3).
        }

        public virtual void Resume()
        {
            if (Status == EncoderStatus.Stop)
                return;

            Status = EncoderStatus.Start;
        }

        public virtual void Pause()
        {
            if (Status == EncoderStatus.Stop)
                return;

            Status = EncoderStatus.Pause;
        }

        public virtual void Stop()
        {
            if (!IsRunning)
                return;

            var stoppedArgs = new EncoderStoppedEventArgs(_videoFramesCount, _audioSamplesCount, _url);

            VideoFramesCount = 0;
            AudioSamplesCount = 0;
            Url = "";
            Status = EncoderStatus.Stop;

            OnEncoderStopped(stoppedArgs);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stop();
            }
        }

        #endregion

        #region Events

        public event EncoderStoppedEventHandler EncoderStopped;

        protected virtual void OnEncoderStopped(EncoderStoppedEventArgs args)
        {
            EncoderStopped?.Invoke(this, args);
        }

        public event EventHandler EncoderFirstStarting;

        protected virtual void OnEncoderFirstStarting()
        {
            EncoderFirstStarting?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
