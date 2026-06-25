using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using MediaEncoder;

namespace ScreenRecorder.Encoder
{
    /// <summary>Video stream parameters for a <see cref="Recorder"/>.</summary>
    public sealed class VideoParams
    {
        public VideoCodec Codec;
        public HwAccel Hw;
        public int Width;
        public int Height;
        public int FpsNumerator;
        public int FpsDenominator = 1;
        public int Bitrate;
        public RateControl RateControl = RateControl.Cbr;
    }

    /// <summary>Audio stream parameters for a <see cref="Recorder"/> (null on the recorder = no audio).</summary>
    public sealed class AudioParams
    {
        public AudioCodec Codec;
        public int SampleRate;
        public int Channels;
        public int Bitrate;
        public SampleFormat Format;
    }

    /// <summary>
    /// File recorder: a video-encode thread and (optional) audio-encode thread feed encoded
    /// packets into one queue that a mux thread drains to the container, ordered by PTS/DTS.
    /// Producers call <see cref="PushVideoFrame"/>/<see cref="PushAudioFrame"/>; the raw data is
    /// copied into a pool on push, so producers may reuse their buffers immediately. CFR: video
    /// PTS is the ideal-grid frame index derived from each frame's QPC timestamp and a single t0.
    /// </summary>
    public sealed unsafe class Recorder : IDisposable
    {
        private readonly struct QueuedPacket
        {
            public readonly bool IsVideo;
            public readonly long Pts;
            public readonly long Dts;
            public readonly nint Data;
            public readonly int Size;
            public readonly bool IsKey;

            public QueuedPacket(bool isVideo, long pts, long dts, nint data, int size, bool isKey)
            {
                IsVideo = isVideo;
                Pts = pts;
                Dts = dts;
                Data = data;
                Size = size;
                IsKey = isKey;
            }
        }

        private readonly string _url;
        private readonly string _format;
        private readonly VideoParams _videoParams;
        private readonly AudioParams _audioParams;

        private readonly BlockingCollection<RawVideoFrame> _videoIn = new BlockingCollection<RawVideoFrame>();
        private readonly BlockingCollection<RawAudioFrame> _audioIn = new BlockingCollection<RawAudioFrame>();
        private readonly BlockingCollection<QueuedPacket> _encodedOut = new BlockingCollection<QueuedPacket>();

        private readonly UnmanagedBufferPool _videoFramePool = new UnmanagedBufferPool();
        private readonly UnmanagedBufferPool _audioFramePool = new UnmanagedBufferPool();
        private readonly UnmanagedBufferPool _packetPool = new UnmanagedBufferPool();

        private FFmpegVideoEncoder _videoEncoder;
        private FFmpegAudioEncoder _audioEncoder;
        private FFmpegFileContainer _container;

        private Thread _videoThread;
        private Thread _audioThread;
        private Thread _muxThread;

        private long _droppedFrames;
        private long _recordedVideoFrames;
        private bool _started;
        private bool _disposed;

        public Recorder(string url, string formatShortName, VideoParams video, AudioParams audio)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _format = formatShortName ?? throw new ArgumentNullException(nameof(formatShortName));
            _videoParams = video ?? throw new ArgumentNullException(nameof(video));
            _audioParams = audio;
        }

        public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

        /// <summary>Video frames accepted so far (drives the recording-time display). Advances at fps.</summary>
        public long RecordedVideoFrames => Interlocked.Read(ref _recordedVideoFrames);

        public void Start()
        {
            if (_started)
                return;
            _started = true;

            _container = new FFmpegFileContainer(_format);

            _videoEncoder = new FFmpegVideoEncoder(
                _videoParams.Codec, _videoParams.Hw, _videoParams.Width, _videoParams.Height,
                _videoParams.FpsNumerator, _videoParams.FpsDenominator, _videoParams.Bitrate,
                _videoParams.RateControl, _container.GlobalHeader);

            if (_audioParams != null)
            {
                _audioEncoder = new FFmpegAudioEncoder(
                    _audioParams.Codec, _audioParams.Channels, _audioParams.SampleRate,
                    _audioParams.Format, _audioParams.Bitrate, _container.GlobalHeader);
            }

            _container.Initialize(_url, _videoEncoder, _audioEncoder);

            _videoThread = new Thread(VideoLoop) { Name = "RecorderVideo", IsBackground = true };
            _videoThread.Start();

            if (_audioEncoder != null)
            {
                _audioThread = new Thread(AudioLoop) { Name = "RecorderAudio", IsBackground = true };
                _audioThread.Start();
            }

            _muxThread = new Thread(MuxLoop) { Name = "RecorderMux", IsBackground = true };
            _muxThread.Start();
        }

        public void PushVideoFrame(in RawVideoFrame frame)
        {
            if (!_started || _videoIn.IsAddingCompleted)
                return;

            // P1: no drop (deterministic harness output). The live-capture drop/backpressure
            // policy lands in P4 when a real WGC source can outrun the encoder.
            int frameBytes = frame.Stride * frame.Height + frame.Stride * (frame.Height / 2); // NV12
            nint buffer = _videoFramePool.Rent(frameBytes);
            Unsafe.CopyBlockUnaligned((void*)buffer, (void*)frame.Data, (uint)frameBytes);

            _videoIn.Add(new RawVideoFrame(buffer, frame.Stride, frame.Width, frame.Height, frame.Format, frame.TimestampQpc));
            Interlocked.Increment(ref _recordedVideoFrames);
        }

        public void PushAudioFrame(in RawAudioFrame frame)
        {
            if (!_started || _audioEncoder == null || _audioIn.IsAddingCompleted)
                return;

            int bytesPerSample = frame.Format == SampleFormat.S16 ? 2 : frame.Format == SampleFormat.S32 || frame.Format == SampleFormat.FLT ? 4 : 1;
            int frameBytes = frame.Samples * frame.Channels * bytesPerSample;
            nint buffer = _audioFramePool.Rent(frameBytes);
            Unsafe.CopyBlockUnaligned((void*)buffer, (void*)frame.Data, (uint)frameBytes);

            _audioIn.Add(new RawAudioFrame(buffer, frame.Samples, frame.SampleRate, frame.Channels, frame.Format, frame.TimestampQpc));
        }

        private void VideoLoop()
        {
            long t0 = long.MinValue;
            long fpsNum = _videoParams.FpsNumerator;
            long fpsDen = _videoParams.FpsDenominator;
            long freq = Stopwatch.Frequency;

            byte** planes = stackalloc byte*[2];
            int* linesizes = stackalloc int[2];

            foreach (var frame in _videoIn.GetConsumingEnumerable())
            {
                if (t0 == long.MinValue)
                    t0 = frame.TimestampQpc;

                long gridIndex = (long)Math.Round((double)(frame.TimestampQpc - t0) * fpsNum / (fpsDen * freq));

                planes[0] = (byte*)frame.Data;
                planes[1] = (byte*)(frame.Data + (nint)frame.Stride * frame.Height);
                linesizes[0] = frame.Stride;
                linesizes[1] = frame.Stride;

                _videoEncoder.SendFrame(planes, linesizes, 2, gridIndex);
                DrainVideo();

                _videoFramePool.Return(frame.Data);
            }

            _videoEncoder.FlushSend();
            DrainVideo();
        }

        private void DrainVideo()
        {
            EncodedVideoPacket? packet;
            while ((packet = _videoEncoder.ReceivePacket()) != null)
            {
                var p = packet.Value;
                nint buffer = _packetPool.Rent(p.Size);
                Unsafe.CopyBlockUnaligned((void*)buffer, (void*)p.Data, (uint)p.Size);
                _encodedOut.Add(new QueuedPacket(true, p.PresentationTimeStamp, p.DecodingTimeStamp, buffer, p.Size, p.IsKeyFrame));
            }
        }

        private void AudioLoop()
        {
            byte** planes = stackalloc byte*[1];

            foreach (var frame in _audioIn.GetConsumingEnumerable())
            {
                planes[0] = (byte*)frame.Data;
                _audioEncoder.Accept(planes, 1, frame.Samples);

                while (_audioEncoder.TrySendNextFrame())
                    DrainAudio();

                _audioFramePool.Return(frame.Data);
            }

            _audioEncoder.Flush();
            DrainAudio();
        }

        private void DrainAudio()
        {
            EncodedAudioPacket? packet;
            while ((packet = _audioEncoder.ReceivePacket()) != null)
            {
                var p = packet.Value;
                nint buffer = _packetPool.Rent(p.Size);
                Unsafe.CopyBlockUnaligned((void*)buffer, (void*)p.Data, (uint)p.Size);
                _encodedOut.Add(new QueuedPacket(false, p.PresentationTimeStamp, p.DecodingTimeStamp, buffer, p.Size, false));
            }
        }

        private void MuxLoop()
        {
            foreach (var q in _encodedOut.GetConsumingEnumerable())
            {
                if (q.IsVideo)
                    _container.WriteVideoPacket(new EncodedVideoPacket(q.Pts, q.Dts, q.Data, q.Size, q.IsKey));
                else
                    _container.WriteAudioPacket(new EncodedAudioPacket(q.Pts, q.Dts, q.Data, q.Size));

                _packetPool.Return(q.Data);
            }
        }

        public void Stop()
        {
            if (!_started)
                return;

            _videoIn.CompleteAdding();
            _audioIn.CompleteAdding();

            _videoThread?.Join();
            _audioThread?.Join();

            _encodedOut.CompleteAdding();
            _muxThread?.Join();

            _container?.Dispose();
            _videoEncoder?.Dispose();
            _audioEncoder?.Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            Stop();

            _videoIn.Dispose();
            _audioIn.Dispose();
            _encodedOut.Dispose();
            _videoFramePool.Dispose();
            _audioFramePool.Dispose();
            _packetPool.Dispose();
        }
    }
}
