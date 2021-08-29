using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
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
            this.VideoFramesCount = videoFramesCount;
            this.AudioSamplesCount = audioSamplesCount;
            this.Url = url;
        }
    }

    public class Encoder : NotifyPropertyBase, IDisposable
    {
        private class MediaBuffer : IDisposable
        {
            private object videoSyncObject = new object();
            private object audioSyncObject = new object();
            private IVideoSource videoSource;
            private IAudioSource audioSource;
            private bool isDisposed = false;

            private ConcurrentQueue<VideoFrame> srcVideoFrameQueue;


            private ConcurrentQueue<VideoFrame> videoFrameQueue;
            private ConcurrentQueue<AudioFrame> audioFrameQueue;

            private ManualResetEvent enableEvent;

            private Thread videoWorkerThread;
            private Thread audioWorkerThread;
            private ManualResetEvent needToStop;

            private CircularBuffer srcAudioCircularBuffer;
            private Resampler resampler;

            public MediaBuffer(IVideoSource videoSource, IAudioSource audioSource)
            {
                this.enableEvent = new ManualResetEvent(false);
                this.videoSource = videoSource;
                this.audioSource = audioSource;

                if (this.videoSource != null)
                {
                    this.videoFrameQueue = new ConcurrentQueue<VideoFrame>();
                    this.srcVideoFrameQueue = new ConcurrentQueue<VideoFrame>();
                    this.videoSource.NewVideoFrame += VideoSource_NewVideoFrame;
                    videoWorkerThread = new Thread(new ThreadStart(VideoWorkerThreadHandler)) { IsBackground = true };
                }
                if (this.audioSource != null)
                {
                    resampler = new Resampler();
                    int framePerBytes = (int)((48000.0d / AppConstants.Framerate) * 4);

                    this.srcAudioCircularBuffer = new CircularBuffer(framePerBytes * 15);
                    this.audioFrameQueue = new ConcurrentQueue<AudioFrame>();
                    this.audioSource.NewAudioPacket += AudioSource_NewAudioPacket;
                    audioWorkerThread = new Thread(new ThreadStart(AudioWorkerThreadHandler)) { IsBackground = true };
                }

                needToStop = new ManualResetEvent(false);

                videoWorkerThread?.Start();
                audioWorkerThread?.Start();
            }

            private void AudioWorkerThreadHandler()
            {
                int samples = (int)(48000.0d / AppConstants.Framerate);

                // 최소 sample수가 1600이 되도록 유지해줌. (Aac 코덱에서 최소 샘플수가 1024 이므로 이것보다 적게 인코더에 넣으면 문제 발생)
                // 해당 인코더에서 처리하려 했으나, 그냥 샘플을 공급할때 많이 주는게 구현에 더 용이하여 이렇게 함
                int skipFrames = (int)(Math.Ceiling(1600.0d / samples) - 1);
                samples *= (skipFrames + 1);

                long skipCount = skipFrames;
                IntPtr audioBuffer = Marshal.AllocHGlobal(samples * 4);
                using (SystemClockEvent systemClockEvent = new SystemClockEvent())
                {
                    while (!needToStop.WaitOne(0, false))
                    {
                        if (systemClockEvent.WaitOne(10))
                        {
                            if (!(enableEvent?.WaitOne(0, false) ?? true))
                                continue;

                            if (audioFrameQueue.Count > 300)
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

                            if (srcAudioCircularBuffer.Count >= (samples * 4))
                            {
                                srcAudioCircularBuffer.Read(audioBuffer, (samples * 4));

                                AudioFrame audioFrame = new AudioFrame(48000, 2, SampleFormat.S16, samples);
                                audioFrame.FillData(audioBuffer);

                                audioFrameQueue.Enqueue(audioFrame);
                            }
                            else
                            {
                                AudioFrame audioFrame = new AudioFrame(48000, 2, SampleFormat.S16, samples);
                                audioFrame.ClearData();
                                audioFrameQueue.Enqueue(audioFrame);
                            }
                        }
                    }
                }
                Marshal.FreeHGlobal(audioBuffer);
            }

            private void VideoWorkerThreadHandler()
            {
                VideoFrame lastVideoFrame = null;

                using (SystemClockEvent systemClockEvent = new SystemClockEvent())
                {
                    while (!needToStop.WaitOne(0, false))
                    {
                        if (systemClockEvent.WaitOne(10))
                        {
                            if (!(enableEvent?.WaitOne(0, false) ?? true))
                                continue;

                            if (videoFrameQueue.Count > 300) // max buffer
                            {
                                continue;
                            }

                            if (srcVideoFrameQueue.TryDequeue(out VideoFrame videoFrame))
                            {
                                if (srcVideoFrameQueue.Count > 3)
                                {
                                    for (int i = 0; i < 3; i++)
                                    {
                                        if (srcVideoFrameQueue.TryDequeue(out VideoFrame temp))
                                            temp.Dispose();
                                    }
                                }

                                lastVideoFrame?.Dispose();
                                lastVideoFrame = new VideoFrame(videoFrame);
                                videoFrameQueue.Enqueue(videoFrame);
                            }
                            else if (lastVideoFrame != null)
                            {
                                VideoFrame clone = new VideoFrame(lastVideoFrame);
                                videoFrameQueue.Enqueue(lastVideoFrame);
                                lastVideoFrame = clone;
                            }
                            else
                            {
                                videoFrameQueue.Enqueue(new VideoFrame(1920, 1080, PixelFormat.RGB24));
                            }
                        }
                    }
                }
                lastVideoFrame?.Dispose();
            }

            public void Start()
            {
                if (enableEvent != null)
                {
                    while (audioFrameQueue?.Count > 0)
                    {
                        if (audioFrameQueue.TryDequeue(out AudioFrame audioFrame))
                            audioFrame.Dispose();
                    }
                    while (videoFrameQueue?.Count > 0)
                    {
                        if (videoFrameQueue.TryDequeue(out VideoFrame videoFrame))
                            videoFrame.Dispose();
                    }

                    if (!enableEvent.WaitOne(0, false))
                        enableEvent.Set();
                }
            }

            public void Stop()
            {
                if (enableEvent != null)
                {
                    if (enableEvent.WaitOne(0, false))
                    {
                        enableEvent.Reset();
                    }
                }
            }

            private void VideoSource_NewVideoFrame(object sender, NewVideoFrameEventArgs eventArgs)
            {
                lock (videoSyncObject)
                {
                    if (isDisposed)
                        return;

                    if (enableEvent != null && !enableEvent.WaitOne(0, false))
                        return;

                    VideoFrame videoFrame = new VideoFrame(eventArgs.Width, eventArgs.Height, eventArgs.PixelFormat);
                    if (eventArgs.PixelFormat == PixelFormat.NV12)
                    {
                        videoFrame.FillData(new IntPtr[] { eventArgs.DataPointer, eventArgs.DataPointer + (eventArgs.Stride * eventArgs.Height) }, new int[] { eventArgs.Stride, eventArgs.Stride, eventArgs.Stride, eventArgs.Stride, eventArgs.Stride, eventArgs.Stride, eventArgs.Stride, eventArgs.Stride });
                    }
                    else
                    {
                        videoFrame.FillData(eventArgs.DataPointer, eventArgs.Stride);
                    }
                    srcVideoFrameQueue.Enqueue(videoFrame);
                }
            }

            private void AudioSource_NewAudioPacket(object sender, NewAudioPacketEventArgs eventArgs)
            {
                lock (audioSyncObject)
                {
                    if (isDisposed)
                        return;

                    if (enableEvent != null && !enableEvent.WaitOne(0, false))
                        return;

                    resampler.Resampling(eventArgs.Channels, eventArgs.SampleFormat, eventArgs.SampleRate,
                        2, SampleFormat.S16, 48000, eventArgs.DataPointer, eventArgs.Samples, out IntPtr destData, out int destSamples);

                    srcAudioCircularBuffer.Write(destData, 0, destSamples * 4);
                    //srcAudioCircularBuffer.Write(eventArgs.DataPointer, 0, eventArgs.Samples * 4);
                }
            }

            public void Dispose()
            {
                needToStop?.Set();
                if (videoWorkerThread != null)
                {
                    if (videoWorkerThread.IsAlive && !videoWorkerThread.Join(2000))
                        videoWorkerThread.Abort();

                    videoWorkerThread = null;
                }
                if (audioWorkerThread != null)
                {
                    if (audioWorkerThread.IsAlive && !audioWorkerThread.Join(500))
                        audioWorkerThread.Abort();

                    audioWorkerThread = null;
                }
                needToStop?.Close();
                needToStop = null;

                lock (videoSyncObject)
                {
                    lock (audioSyncObject)
                    {
                        if (this.videoSource != null)
                            this.videoSource.NewVideoFrame -= VideoSource_NewVideoFrame;
                        while (srcVideoFrameQueue?.Count > 0)
                        {
                            if (srcVideoFrameQueue.TryDequeue(out VideoFrame videoFrame))
                                videoFrame.Dispose();
                        }

                        if (this.audioSource != null)
                            this.audioSource.NewAudioPacket -= AudioSource_NewAudioPacket;
                        while (audioFrameQueue?.Count > 0)
                        {
                            if (audioFrameQueue.TryDequeue(out AudioFrame audioFrame))
                                audioFrame.Dispose();
                        }

                        while (videoFrameQueue?.Count > 0)
                        {
                            if (videoFrameQueue.TryDequeue(out VideoFrame videoFrame))
                                videoFrame.Dispose();
                        }

                        resampler?.Dispose();
                        resampler = null;

                        isDisposed = true;
                    }
                }
            }

            public VideoFrame TryVideoFrameDequeue()
            {
                if (videoFrameQueue != null && videoFrameQueue.TryDequeue(out VideoFrame videoFrame))
                {
                    return videoFrame;
                }
                return null;
            }

            public AudioFrame TryAudioFrameDequeue()
            {
                if (audioFrameQueue != null && audioFrameQueue.TryDequeue(out AudioFrame audioFrame))
                {
                    return audioFrame;
                }
                return null;
            }
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

        private ulong videoFramesCount;
        public ulong VideoFramesCount
        {
            get => videoFramesCount;
            private set
            {
                SetProperty(ref videoFramesCount, value);
                VideoTime = Utils.VideoFramesCountToSeconds(value);
            }
        }

        private ulong videoTime;
        public ulong VideoTime
        {
            get => videoTime;
            private set
            {
                SetProperty(ref videoTime, value);
            }
        }

        private ulong audioSamplesCount;
        public ulong AudioSamplesCount
        {
            get => audioSamplesCount;
            private set => SetProperty(ref audioSamplesCount, value);
        }

        private string url;
        public string Url
        {
            get => url;
            private set => SetProperty(ref url, value);
        }

        private bool isStarted = false;
        public bool IsStarted
        {
            get => isStarted;
            private set
            {
                SetProperty(ref isStarted, value);
            }
        }

        private bool isPaused = false;
        public bool IsPaused
        {
            get => isPaused;
            private set
            {
                SetProperty(ref isPaused, value);
            }
        }

        private bool isStopped = true;
        public bool IsStopped
        {
            get => isStopped;
            private set
            {
                SetProperty(ref isStopped, value);
            }
        }

        private EncoderStatus status = EncoderStatus.Stop;
        public EncoderStatus Status
        {
            get => status;
            private set
            {
                if (SetProperty(ref status, value))
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
                if (workerThread != null)
                {
                    if (workerThread.IsAlive && workerThread.Join(0) == false)
                        return true;

                    workerThread = null;

                    if (needToStop != null)
                        needToStop.Close();
                    needToStop = null;
                }
                return false;
            }
        }

        private ulong maximumVideoFramesCount = 0;
        public ulong MaximumVideoFramesCount
        {
            get => maximumVideoFramesCount;
            set
            {
                SetProperty(ref maximumVideoFramesCount, value);
            }
        }

        public event EncoderStoppedEventHandler EncoderStopped;

        private Thread workerThread = null;
        private ManualResetEvent needToStop = null;

        public void Start(string format, string url, IVideoSource videoSource, VideoCodec videoCodec, int videoBitrate, VideoSize videoSize, IAudioSource audioSource, AudioCodec audioCodec, int audioBitrate)
        {
            if (IsRunning)
                return;

            Url = url;

            Status = EncoderStatus.Start;

            needToStop = new ManualResetEvent(false);
            workerThread = new Thread(new ParameterizedThreadStart(WorkerThreadHandler)) { Name = "MediaEncoder", IsBackground = true };
            workerThread.Start(new EncoderArguments()
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

            if (needToStop != null)
            {
                needToStop.Set();
            }
            if (workerThread != null)
            {
                if (workerThread.IsAlive && !workerThread.Join(3000))
                    workerThread.Abort();
                workerThread = null;

                if (needToStop != null)
                    needToStop.Close();
                needToStop = null;
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
                    using (MediaBuffer mediaBuffer = new MediaBuffer(encoderArguments.VideoSource, encoderArguments.AudioSource))
                    {
                        using (MediaWriter mediaWriter = new MediaWriter(
                            encoderArguments.VideoSize.Width, encoderArguments.VideoSize.Height, (int)AppConstants.Framerate, 1,
                            encoderArguments.VideoCodec, encoderArguments.VideoBitrate,
                            encoderArguments.AudioCodec, encoderArguments.AudioBitrate))
                        {
                            mediaWriter.Open(encoderArguments.Url, encoderArguments.Format);

                            mediaBuffer.Start();
                            while (!needToStop.WaitOne(0, false))
                            {
                                VideoFrame videoFrame = mediaBuffer.TryVideoFrameDequeue();
                                AudioFrame audioFrame = mediaBuffer.TryAudioFrameDequeue();
                                if (videoFrame != null || audioFrame != null)
                                {
                                    if (videoFrame != null)
                                    {
                                        if (status != EncoderStatus.Pause)
                                        {
                                            mediaWriter.EncodeVideoFrame(videoFrame);
                                            VideoFramesCount = mediaWriter.VideoFramesCount;
                                        }

                                        if (maximumVideoFramesCount > 0 && maximumVideoFramesCount <= videoFramesCount)
                                        {
                                            needToStop?.Set();
                                        }

                                        videoFrame.Dispose();
                                    }
                                    if (audioFrame != null)
                                    {
                                        if (status != EncoderStatus.Pause)
                                        {
                                            mediaWriter.EncodeAudioFrame(audioFrame);
                                            AudioSamplesCount = mediaWriter.AudioSamplesCount;
                                        }

                                        audioFrame.Dispose();
                                    }
                                }
                                else
                                {
                                    if (needToStop.WaitOne(1, false))
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
                OnEncoderStopped(new EncoderStoppedEventArgs(videoFramesCount, audioSamplesCount, url));
                EncoderStopped?.Invoke(this, new EncoderStoppedEventArgs(videoFramesCount, audioSamplesCount, url));
                VideoFramesCount = 0;
                AudioSamplesCount = 0;
                Url = "";
                Status = EncoderStatus.Stop;
            }
        }

        protected virtual void OnEncoderStopped(EncoderStoppedEventArgs args) { }

        public void Dispose()
        {
            Stop();
        }
    }
}
