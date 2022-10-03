using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using MediaEncoder;
using ScreenRecorder.AudioSource;
using ScreenRecorder.VideoSource;

namespace ScreenRecorder.Encoder
{
    public delegate void EncoderStoppedEventHandler(object sender, EncoderStoppedEventArgs eventArgs);

    public class EncoderStoppedEventArgs : EventArgs
    {
        public ulong VideoFramesCount { get; private set; }
        public ulong AudioSamplesCount { get; private set; }
        public string Url { get; private set; }

        public EncoderStoppedEventArgs(ulong videoFramesCount, ulong audioSamplesCount, string url)
        {
            VideoFramesCount = videoFramesCount;
            AudioSamplesCount = audioSamplesCount;
            Url = url;
        }
    }

    public class Encoder : NotifyPropertyBase, IDisposable
    {
        private class MediaBuffer : IDisposable
        {
            #region Fields

            private readonly object _videoSyncObject = new object();
            private readonly object _audioSyncObject = new object();
            private readonly IVideoSource _videoSource;
            private readonly IAudioSource _audioSource;
            private bool _isDisposed = false;

            private readonly ConcurrentQueue<VideoFrame> _srcVideoFrameQueue;

            private readonly ConcurrentQueue<VideoFrame> _videoFrameQueue;
            private readonly ConcurrentQueue<AudioFrame> _audioFrameQueue;

            private readonly ManualResetEvent _enableEvent;

            private Thread _videoWorkerThread;
            private Thread _audioWorkerThread;
            private ManualResetEvent _needToStop;

            private readonly CircularBuffer _srcAudioCircularBuffer;
            private Resampler _resampler;

            private readonly int _samplesPerFrame;
            private readonly int _samplesBytesPerFrame;
            private readonly int _framesPerAdditinalSample;

            #endregion

            #region Constructors

            public MediaBuffer(IVideoSource videoSource, IAudioSource audioSource)
            {
                _enableEvent = new ManualResetEvent(false);
                _videoSource = videoSource;
                _audioSource = audioSource;

                if (_videoSource != null)
                {
                    _videoFrameQueue = new ConcurrentQueue<VideoFrame>();
                    _srcVideoFrameQueue = new ConcurrentQueue<VideoFrame>();
                    _videoSource.NewVideoFrame += VideoSource_NewVideoFrame;
                    _videoWorkerThread = new Thread(new ThreadStart(VideoWorkerThreadHandler)) { IsBackground = true };
                }
                if (_audioSource != null)
                {
                    _resampler = new Resampler();

                    _samplesPerFrame = (int)(48000.0d / VideoClockEvent.Framerate);
                    _samplesBytesPerFrame = _samplesPerFrame * 2 * 2; // 2Ch, 16bit

                    var remainingSamples = 48000 - (_samplesPerFrame * VideoClockEvent.Framerate);
                    _framesPerAdditinalSample = remainingSamples != 0 ? VideoClockEvent.Framerate / remainingSamples : 0;

                    _srcAudioCircularBuffer = new CircularBuffer(_samplesBytesPerFrame * 15);
                    _audioFrameQueue = new ConcurrentQueue<AudioFrame>();
                    _audioSource.NewAudioPacket += AudioSource_NewAudioPacket;
                    _audioWorkerThread = new Thread(new ThreadStart(AudioWorkerThreadHandler)) { IsBackground = true };
                }

                _needToStop = new ManualResetEvent(false);

                _videoWorkerThread?.Start();
                _audioWorkerThread?.Start();
            }

            #endregion

            #region Helpers

            private void AudioWorkerThreadHandler()
            {
                int samplesPerAudioFrame = _samplesPerFrame;

                // Keep the minimum number of samples at 1600 (Aac codec has a minimum number of samples, so less than this will cause problems)
                // I tried to process it on the encoder, but it's easier to implement if I just supply a lot of samples.
                int skipFrames = (int)(Math.Ceiling(1600.0d / samplesPerAudioFrame) - 1);
                samplesPerAudioFrame *= skipFrames + 1;

                var samplesBytesPerFrame = samplesPerAudioFrame * 2 * 2;

                long skipCount = skipFrames;
                IntPtr audioBuffer = Marshal.AllocHGlobal(samplesBytesPerFrame + (VideoClockEvent.Framerate * 2 * 2));
                using (VideoClockEvent videoClockEvent = new VideoClockEvent())
                {
                    long frames = 0, lastReadedFrames = 0;
                    while (!_needToStop.WaitOne(0, false))
                    {
                        if (videoClockEvent.WaitOne(10))
                        {
                            frames++;

                            if (!(_enableEvent?.WaitOne(0, false) ?? true))
                                continue;

                            /// Frames can stack if the PC momentarily slows down and the encoding speed drops below x1.
                            /// This will cause the recorded image to be disconnected, so it is specified that it can be buffered up to 300 frames.
                            if (_audioFrameQueue.Count > 300)
                            {
                                continue;
                            }

                            if (skipCount-- > 0)
                            {
                                continue;
                            }
                            else
                            {
                                skipCount = skipFrames;
                            }

                            var needAdditinalSamples = _framesPerAdditinalSample != 0 ? Math.Min(VideoClockEvent.Framerate, (int)(lastReadedFrames - frames) % _framesPerAdditinalSample) : 0;
                            var needSamplesBytes = samplesBytesPerFrame + (needAdditinalSamples * 4);

                            if (_srcAudioCircularBuffer.Count >= needSamplesBytes)
                            {
                                _srcAudioCircularBuffer.Read(audioBuffer, needSamplesBytes);

                                AudioFrame audioFrame = new AudioFrame(48000, 2, SampleFormat.S16, needSamplesBytes / 4);
                                audioFrame.FillFrame(audioBuffer);

                                _audioFrameQueue.Enqueue(audioFrame);
                            }
                            else
                            {
                                AudioFrame audioFrame = new AudioFrame(48000, 2, SampleFormat.S16, needSamplesBytes / 4);
                                audioFrame.ClearFrame();
                                _audioFrameQueue.Enqueue(audioFrame);
                            }

                            lastReadedFrames = frames;
                        }
                    }
                }
                Marshal.FreeHGlobal(audioBuffer);
            }

            private void VideoWorkerThreadHandler()
            {
                VideoFrame lastVideoFrame = null;

                using (VideoClockEvent videoClockEvent = new VideoClockEvent())
                {
                    while (!_needToStop.WaitOne(0, false))
                    {
                        if (videoClockEvent.WaitOne(10))
                        {
                            if (!(_enableEvent?.WaitOne(0, false) ?? true))
                                continue;

                            /// Frames can stack if the PC momentarily slows down and the encoding speed drops below x1.
                            /// This will cause the recorded image to be disconnected, so it is specified that it can be buffered up to 300 frames.
                            if (_videoFrameQueue.Count > 300) // max buffer
                            {
                                continue;
                            }

                            if (_srcVideoFrameQueue.TryDequeue(out VideoFrame videoFrame))
                            {
                                if (_srcVideoFrameQueue.Count > 3)
                                {
                                    for (int i = 0; i < 3; i++)
                                    {
                                        if (_srcVideoFrameQueue.TryDequeue(out VideoFrame temp))
                                            temp.Dispose();
                                    }
                                }

                                lastVideoFrame?.Dispose();
                                lastVideoFrame = new VideoFrame(videoFrame);
                                _videoFrameQueue.Enqueue(videoFrame);
                            }
                            else if (lastVideoFrame != null)
                            {
                                VideoFrame clone = new VideoFrame(lastVideoFrame);
                                _videoFrameQueue.Enqueue(lastVideoFrame);
                                lastVideoFrame = clone;
                            }
                            else
                            {
                                _videoFrameQueue.Enqueue(new VideoFrame(1920, 1080, PixelFormat.RGB24));
                            }
                        }
                    }
                }
                lastVideoFrame?.Dispose();
            }

            public void Start()
            {
                if (_enableEvent != null)
                {
                    while (_audioFrameQueue?.Count > 0)
                    {
                        if (_audioFrameQueue.TryDequeue(out AudioFrame audioFrame))
                            audioFrame.Dispose();
                    }
                    while (_videoFrameQueue?.Count > 0)
                    {
                        if (_videoFrameQueue.TryDequeue(out VideoFrame videoFrame))
                            videoFrame.Dispose();
                    }

                    if (!_enableEvent.WaitOne(0, false))
                        _enableEvent.Set();
                }
            }

            public void Stop()
            {
                if (_enableEvent != null)
                {
                    if (_enableEvent.WaitOne(0, false))
                    {
                        _enableEvent.Reset();
                    }
                }
            }

            private void VideoSource_NewVideoFrame(object sender, NewVideoFrameEventArgs eventArgs)
            {
                lock (_videoSyncObject)
                {
                    if (_isDisposed)
                        return;

                    if (_enableEvent != null && !_enableEvent.WaitOne(0, false))
                        return;

                    VideoFrame videoFrame = new VideoFrame(eventArgs.Width, eventArgs.Height, eventArgs.PixelFormat);
                    if (eventArgs.PixelFormat == PixelFormat.NV12)
                    {
                        videoFrame.FillFrame(new IntPtr[] { eventArgs.DataPointer, eventArgs.DataPointer + (eventArgs.Stride * eventArgs.Height) }, new int[] { eventArgs.Stride, eventArgs.Stride, eventArgs.Stride, eventArgs.Stride, eventArgs.Stride, eventArgs.Stride, eventArgs.Stride, eventArgs.Stride });
                    }
                    else
                    {
                        videoFrame.FillFrame(eventArgs.DataPointer, eventArgs.Stride);
                    }
                    _srcVideoFrameQueue.Enqueue(videoFrame);
                }
            }

            private void AudioSource_NewAudioPacket(object sender, NewAudioPacketEventArgs eventArgs)
            {
                lock (_audioSyncObject)
                {
                    if (_isDisposed)
                        return;

                    if (_enableEvent != null && !_enableEvent.WaitOne(0, false))
                        return;

                    if (eventArgs.Channels != 2 || eventArgs.SampleFormat != SampleFormat.S16 || eventArgs.SampleRate != 48000)
                    {
                        _resampler.Resampling(eventArgs.Channels, eventArgs.SampleFormat, eventArgs.SampleRate,
                            2, SampleFormat.S16, 48000, eventArgs.DataPointer, eventArgs.Samples, out var destData, out var destSamples);

                        _srcAudioCircularBuffer.Write(destData, 0, destSamples * 4);
                    }
                    else
                    {
                        _srcAudioCircularBuffer.Write(eventArgs.DataPointer, 0, eventArgs.Samples * 4);
                    }
                }
            }

            public void Dispose()
            {
                _needToStop?.Set();
                if (_videoWorkerThread != null)
                {
                    if (_videoWorkerThread.IsAlive && !_videoWorkerThread.Join(2000))
                        _videoWorkerThread.Abort();

                    _videoWorkerThread = null;
                }
                if (_audioWorkerThread != null)
                {
                    if (_audioWorkerThread.IsAlive && !_audioWorkerThread.Join(500))
                        _audioWorkerThread.Abort();

                    _audioWorkerThread = null;
                }
                _needToStop?.Close();
                _needToStop = null;

                lock (_videoSyncObject)
                {
                    lock (_audioSyncObject)
                    {
                        if (_videoSource != null)
                            _videoSource.NewVideoFrame -= VideoSource_NewVideoFrame;
                        while (_srcVideoFrameQueue?.Count > 0)
                        {
                            if (_srcVideoFrameQueue.TryDequeue(out VideoFrame videoFrame))
                                videoFrame.Dispose();
                        }

                        if (_audioSource != null)
                            _audioSource.NewAudioPacket -= AudioSource_NewAudioPacket;
                        while (_audioFrameQueue?.Count > 0)
                        {
                            if (_audioFrameQueue.TryDequeue(out AudioFrame audioFrame))
                                audioFrame.Dispose();
                        }

                        while (_videoFrameQueue?.Count > 0)
                        {
                            if (_videoFrameQueue.TryDequeue(out VideoFrame videoFrame))
                                videoFrame.Dispose();
                        }

                        _resampler?.Dispose();
                        _resampler = null;

                        _isDisposed = true;
                    }
                }
            }

            public VideoFrame TryVideoFrameDequeue()
            {
                if (_videoFrameQueue != null && _videoFrameQueue.TryDequeue(out VideoFrame videoFrame))
                {
                    return videoFrame;
                }
                return null;
            }

            public AudioFrame TryAudioFrameDequeue()
            {
                if (_audioFrameQueue != null && _audioFrameQueue.TryDequeue(out AudioFrame audioFrame))
                {
                    return audioFrame;
                }
                return null;
            }

            #endregion
        }

        private class EncoderArguments
        {
            public IVideoSource VideoSource { get; set; }
            public IAudioSource AudioSource { get; set; }
            public string Format { get; set; }
            public string Url { get; set; }

            public VideoCodec VideoCodec { get; set; }
            public AudioCodec AudioCodec { get; set; }

            public int VideoBitrate { get; set; }
            public int AudioBitrate { get; set; }
            public VideoSize VideoSize { get; set; }
        }

        #region Bindable Properties

        private ulong _videoFramesCount;

        public ulong VideoFramesCount
        {
            get => _videoFramesCount;
            private set
            {
                SetProperty(ref _videoFramesCount, value);
                VideoTime = Utils.VideoFramesCountToSeconds(value);
            }
        }

        private ulong _videoTime;

        public ulong VideoTime
        {
            get => _videoTime;
            private set
            {
                SetProperty(ref _videoTime, value);
            }
        }

        private ulong _audioSamplesCount;

        public ulong AudioSamplesCount
        {
            get => _audioSamplesCount;
            private set => SetProperty(ref _audioSamplesCount, value);
        }

        private string _url;

        public string Url
        {
            get => _url;
            private set => SetProperty(ref _url, value);
        }

        private bool _isStarted = false;

        public bool IsStarted
        {
            get => _isStarted;
            private set
            {
                SetProperty(ref _isStarted, value);
            }
        }

        private bool _isPaused = false;

        public bool IsPaused
        {
            get => _isPaused;
            private set
            {
                SetProperty(ref _isPaused, value);
            }
        }

        private bool _isStopped = true;

        public bool IsStopped
        {
            get => _isStopped;
            private set
            {
                SetProperty(ref _isStopped, value);
            }
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

        public bool IsRunning
        {
            get
            {
                if (_workerThread != null)
                {
                    if (_workerThread.IsAlive && _workerThread.Join(0) == false)
                        return true;

                    _workerThread = null;

                    if (_needToStop != null)
                        _needToStop.Close();
                    _needToStop = null;
                }
                return false;
            }
        }

        private ulong _maximumVideoFramesCount = 0;

        public ulong MaximumVideoFramesCount
        {
            get => _maximumVideoFramesCount;
            set
            {
                SetProperty(ref _maximumVideoFramesCount, value);
            }
        }

        #endregion

        #region Helpers

        private Thread _workerThread = null;

        private ManualResetEvent _needToStop = null;

        public void Start(string format, string url, IVideoSource videoSource, VideoCodec videoCodec, int videoBitrate, VideoSize videoSize, IAudioSource audioSource, AudioCodec audioCodec, int audioBitrate)
        {
            if (IsRunning)
                return;

            Url = url;

            Status = EncoderStatus.Start;

            OnEncoderFirstStarting();

            _needToStop = new ManualResetEvent(false);
            _workerThread = new Thread(new ParameterizedThreadStart(WorkerThreadHandler)) { Name = "Encoder", IsBackground = true };
            _workerThread.Start(new EncoderArguments()
            {
                VideoSource = videoSource,
                AudioSource = audioSource,
                Format = format,
                Url = url,
                VideoCodec = videoCodec,
                VideoBitrate = videoBitrate,
                VideoSize = videoSize,
                AudioCodec = audioCodec,
                AudioBitrate = audioBitrate,
            });
        }

        public void Resume()
        {
            if (!IsRunning && Status != EncoderStatus.Start)
                return;

            Status = EncoderStatus.Start;
        }

        public void Pause()
        {
            if (!IsRunning)
                return;

            Status = EncoderStatus.Pause;
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            if (_needToStop != null)
            {
                _needToStop.Set();
            }
            if (_workerThread != null)
            {
                if (_workerThread.IsAlive && !_workerThread.Join(3000))
                    _workerThread.Abort();
                _workerThread = null;

                if (_needToStop != null)
                    _needToStop.Close();
                _needToStop = null;
            }

            VideoFramesCount = 0;
            AudioSamplesCount = 0;
            Url = "";
            Status = EncoderStatus.Stop;
        }

        private void WorkerThreadHandler(object argument)
        {
            try
            {
                if (argument is EncoderArguments encoderArguments)
                {
                    using (var mediaBuffer = new MediaBuffer(encoderArguments.VideoSource, encoderArguments.AudioCodec == AudioCodec.None ? null : encoderArguments.AudioSource))
                    {
                        using (var mediaWriter = new MediaWriter(
                            encoderArguments.VideoSize.Width, encoderArguments.VideoSize.Height, VideoClockEvent.Framerate, 1,
                            encoderArguments.VideoCodec, encoderArguments.VideoBitrate,
                            encoderArguments.AudioCodec, encoderArguments.AudioBitrate))
                        {
                            mediaWriter.Open(encoderArguments.Url, encoderArguments.Format);

                            mediaBuffer.Start();
                            while (!_needToStop.WaitOne(0, false))
                            {
                                var videoFrame = mediaBuffer.TryVideoFrameDequeue();
                                var audioFrame = mediaBuffer.TryAudioFrameDequeue();
                                if (videoFrame != null || audioFrame != null)
                                {
                                    if (videoFrame != null)
                                    {
                                        if (_status != EncoderStatus.Pause)
                                        {
                                            mediaWriter.EncodeVideoFrame(videoFrame);
                                            VideoFramesCount = mediaWriter.VideoFramesCount;
                                        }

                                        if (_maximumVideoFramesCount > 0 && _maximumVideoFramesCount <= _videoFramesCount)
                                        {
                                            _needToStop?.Set();
                                        }

                                        videoFrame.Dispose();
                                    }
                                    if (audioFrame != null)
                                    {
                                        if (_status != EncoderStatus.Pause)
                                        {
                                            mediaWriter.EncodeAudioFrame(audioFrame);
                                            AudioSamplesCount = mediaWriter.AudioSamplesCount;
                                        }

                                        audioFrame.Dispose();
                                    }
                                }
                                else
                                {
                                    if (_needToStop.WaitOne(1, false))
                                        break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                OnEncoderStopped(new EncoderStoppedEventArgs(_videoFramesCount, _audioSamplesCount, _url));
                VideoFramesCount = 0;
                AudioSamplesCount = 0;
                Url = "";
                Status = EncoderStatus.Stop;
            }
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
