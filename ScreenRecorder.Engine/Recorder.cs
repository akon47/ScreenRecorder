using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using MediaEncoder;
using ScreenRecorder.DirectX;

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

        /// <summary>Constant-quality value for <see cref="RateControl.Cq"/> (0–51, lower = better).</summary>
        public int Quality = 23;

        /// <summary>
        /// Frames are pushed only when the content changed (gaps between grid indices). The grid
        /// (time base 1/fps) is the same as for constant frame rate.
        /// </summary>
        public bool VariableFrameRate;

        /// <summary>
        /// Shared GPU texture ring for the zero-copy hardware-encode path, or null for CPU
        /// input. If the encoder cannot use it, the recorder silently falls back to CPU input
        /// (check <see cref="Recorder.UsesGpuInput"/> after Start).
        /// </summary>
        internal SharedNv12Ring SharedRing;
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
    /// File recorder: a video-encode thread and an audio-encode thread feed encoded packets
    /// into one queue that a mux thread drains to the container (FFmpeg interleaves by DTS).
    /// Either stream may be absent (audio-only recording).
    ///
    /// Video arrives in one of three forms, all stamped with an ideal-grid index (PTS in the
    /// 1/fps time base): a pooled CPU NV12 buffer whose ownership passes to the recorder, a
    /// shared GPU texture slot (zero-copy hardware path), or a "repeat the last picture" marker
    /// that costs no pixel work. The capture thread never blocks: when the encoder falls
    /// behind, frames are dropped and counted. Encoded packets, in contrast, are never dropped
    /// — a stalled disk back-pressures the encoders instead.
    ///
    /// A failure on any pipeline thread (encoder error, disk full...) is contained: it is
    /// recorded in <see cref="Fault"/>, <see cref="Faulted"/> fires once on a pool thread, and
    /// the threads keep draining so that <see cref="Stop"/> still returns and finalizes
    /// whatever was written.
    /// </summary>
    public sealed unsafe class Recorder : IVideoFrameSink, IDisposable
    {
        private enum VideoWorkKind : byte
        {
            CpuFrame,
            SharedSlot,
            Repeat,
        }

        private readonly struct VideoWork
        {
            public readonly VideoWorkKind Kind;
            public readonly nint Data;
            public readonly int Stride;
            public readonly int Slot;
            public readonly long Pts;

            public VideoWork(VideoWorkKind kind, nint data, int stride, int slot, long pts)
            {
                Kind = kind;
                Data = data;
                Stride = stride;
                Slot = slot;
                Pts = pts;
            }
        }

        private readonly struct AudioWork
        {
            public readonly nint Data;
            public readonly int Samples;

            public AudioWork(nint data, int samples)
            {
                Data = data;
                Samples = samples;
            }
        }

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

        /// <summary>Upper bound on raw CPU frames waiting for the encoder, by memory.</summary>
        private const long MaxPendingVideoBytes = 96L * 1024 * 1024;

        private readonly string _url;
        private readonly string _format;
        private readonly VideoParams _videoParams;
        private readonly AudioParams _audioParams;

        private readonly WorkQueue<VideoWork> _videoIn = new WorkQueue<VideoWork>(64);
        private readonly WorkQueue<AudioWork> _audioIn = new WorkQueue<AudioWork>(256);
        private readonly WorkQueue<QueuedPacket> _encodedOut = new WorkQueue<QueuedPacket>(1024);

        private readonly UnmanagedBufferPool _videoFramePool = new UnmanagedBufferPool();
        private readonly UnmanagedBufferPool _audioFramePool = new UnmanagedBufferPool();
        private readonly UnmanagedBufferPool _packetPool = new UnmanagedBufferPool();

        private FFmpegVideoEncoder _videoEncoder;
        private FFmpegAudioEncoder _audioEncoder;
        private FFmpegFileContainer _container;
        private SharedNv12Ring _ring;

        private Thread _videoThread;
        private Thread _audioThread;
        private Thread _muxThread;

        private Exception _fault;
        private long _droppedFrames;
        private long _recordedVideoFrames;
        private long _encodedVideoFrames;
        private long _bytesWritten;
        private long _pendingVideoBytes;
        private int _maxPendingCpuFrames = 2;
        private long _lastSubmittedPts = -1;
        private long _legacyT0 = long.MinValue;
        private bool _started;
        private bool _stopped;
        private bool _disposed;

        public Recorder(string url, string formatShortName, VideoParams video, AudioParams audio)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _format = formatShortName ?? throw new ArgumentNullException(nameof(formatShortName));
            if (video == null && audio == null)
                throw new ArgumentException("A recording needs video, audio or both.");

            _videoParams = video;
            _audioParams = audio;
        }

        /// <summary>Raised once, on a thread-pool thread, when a pipeline thread fails.</summary>
        public event Action<Exception> Faulted;

        /// <summary>The first pipeline failure, or null.</summary>
        public Exception Fault => Volatile.Read(ref _fault);

        /// <summary>Frames the capture side offered that were discarded because the encoder was behind.</summary>
        public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

        /// <summary>Video frames accepted for encoding so far (new pictures + repeats).</summary>
        public long RecordedVideoFrames => Interlocked.Read(ref _recordedVideoFrames);

        /// <summary>Video frames the encoder has consumed so far.</summary>
        public long EncodedVideoFrames => Interlocked.Read(ref _encodedVideoFrames);

        /// <summary>Bytes written to the output file so far.</summary>
        public long BytesWritten => Interlocked.Read(ref _bytesWritten);

        /// <summary>True when video goes to the encoder as GPU textures (zero-copy path).</summary>
        public bool UsesGpuInput => _videoEncoder != null && _videoEncoder.UsesGpuInput;

        /// <summary>The hardware backend actually in use (after any fallback), or Software.</summary>
        public HwAccel ActualHwAccel { get; private set; } = HwAccel.Software;

        /// <summary>FFmpeg encoder name in use (e.g. <c>h264_nvenc</c>), or null without video.</summary>
        public string VideoEncoderName => _videoEncoder?.EncoderName;

        /// <summary>True when the file was finalized without error (valid after Stop).</summary>
        public bool CompletedSuccessfully { get; private set; }

        /// <summary>
        /// True after <see cref="Stop"/> when not a single packet reached the file (stopped
        /// before the first frame, say): the file holds nothing playable.
        /// </summary>
        public bool IsEmpty { get; private set; }

        public bool HasVideo => _videoParams != null;

        public bool HasAudio => _audioParams != null;

        public void Start()
        {
            if (_started)
                return;
            _started = true;

            try
            {
                _container = new FFmpegFileContainer(_format);

                if (_videoParams != null)
                {
                    _videoEncoder = CreateVideoEncoder(_container.GlobalHeader);
                    if (_videoEncoder.UsesGpuInput)
                        _ring = _videoParams.SharedRing;

                    long frameBytes = (long)_videoParams.Width * _videoParams.Height * 3 / 2;
                    _maxPendingCpuFrames = (int)Math.Clamp(MaxPendingVideoBytes / Math.Max(1, frameBytes), 2, 8);
                }

                if (_audioParams != null)
                {
                    _audioEncoder = new FFmpegAudioEncoder(
                        _audioParams.Codec, _audioParams.Channels, _audioParams.SampleRate,
                        _audioParams.Format, _audioParams.Bitrate, _container.GlobalHeader);
                }

                _container.Initialize(_url, _videoEncoder, _audioEncoder);
            }
            catch
            {
                DisposePipeline();
                throw;
            }

            if (_videoEncoder != null)
            {
                _videoThread = new Thread(VideoLoop) { Name = "RecorderVideo", IsBackground = true };
                _videoThread.Start();
            }

            if (_audioEncoder != null)
            {
                _audioThread = new Thread(AudioLoop) { Name = "RecorderAudio", IsBackground = true };
                _audioThread.Start();
            }

            _muxThread = new Thread(MuxLoop) { Name = "RecorderMux", IsBackground = true };
            _muxThread.Start();
        }

        /// <summary>
        /// Opens the video encoder with graceful degradation: GPU-texture input → CPU input on
        /// the same hardware encoder → software encoder. A machine-specific hardware failure
        /// therefore costs performance, never the recording.
        /// </summary>
        private FFmpegVideoEncoder CreateVideoEncoder(bool globalHeader)
        {
            var settings = new VideoEncoderSettings
            {
                Codec = _videoParams.Codec,
                Hw = _videoParams.Hw,
                Width = _videoParams.Width,
                Height = _videoParams.Height,
                FpsNumerator = _videoParams.FpsNumerator,
                FpsDenominator = _videoParams.FpsDenominator,
                Bitrate = _videoParams.Bitrate,
                RateControl = _videoParams.RateControl,
                Quality = _videoParams.Quality,
                GlobalHeader = globalHeader,
                VariableFrameRate = _videoParams.VariableFrameRate,
                SharedRing = _videoParams.SharedRing,
            };

            if (settings.SharedRing != null)
            {
                try
                {
                    var gpu = new FFmpegVideoEncoder(settings);
                    ActualHwAccel = settings.Hw;
                    return gpu;
                }
                catch
                {
                    settings.SharedRing = null; // CPU input, same encoder
                }
            }

            if (settings.Hw != HwAccel.Software)
            {
                try
                {
                    var hw = new FFmpegVideoEncoder(settings);
                    ActualHwAccel = settings.Hw;
                    return hw;
                }
                catch
                {
                    settings.Hw = HwAccel.Software;
                }
            }

            var software = new FFmpegVideoEncoder(settings);
            ActualHwAccel = HwAccel.Software;
            return software;
        }

        #region Video input

        /// <summary>
        /// Rents a buffer for one CPU NV12 frame. Fill it, then hand it over with
        /// <see cref="SubmitVideoBuffer"/> (or give it back with <see cref="ReturnVideoBuffer"/>).
        /// Returns 0 when too many frames are already waiting for the encoder — skip the frame.
        /// </summary>
        public nint RentVideoBuffer(int bytes)
        {
            if (!CanAcceptVideo() || Volatile.Read(ref _pendingVideoBytes) >= (long)_maxPendingCpuFrames * bytes)
            {
                Interlocked.Increment(ref _droppedFrames);
                return 0;
            }

            return _videoFramePool.Rent(bytes);
        }

        public void ReturnVideoBuffer(nint buffer) => _videoFramePool.Return(buffer);

        /// <summary>
        /// Queues a rented buffer holding an NV12 frame (Y plane then UV plane, both with
        /// <paramref name="stride"/>). Ownership passes to the recorder either way; returns
        /// false when the frame was dropped.
        /// </summary>
        public bool SubmitVideoBuffer(nint buffer, int stride, int bytes, long gridIndex)
        {
            if (!CanAcceptVideo() || gridIndex <= _lastSubmittedPts)
            {
                _videoFramePool.Return(buffer);
                Interlocked.Increment(ref _droppedFrames);
                return false;
            }

            Interlocked.Add(ref _pendingVideoBytes, bytes);
            if (!_videoIn.TryEnqueue(new VideoWork(VideoWorkKind.CpuFrame, buffer, stride, bytes, gridIndex)))
            {
                Interlocked.Add(ref _pendingVideoBytes, -bytes);
                _videoFramePool.Return(buffer);
                Interlocked.Increment(ref _droppedFrames);
                return false;
            }

            _lastSubmittedPts = gridIndex;
            Interlocked.Increment(ref _recordedVideoFrames);
            return true;
        }

        /// <summary>
        /// Queues a written shared-ring slot (zero-copy path). The caller must already have
        /// ended the write with <c>queued: true</c>. Returns false when dropped (the slot is
        /// released back to the ring).
        /// </summary>
        internal bool SubmitSharedSlot(int slot, long gridIndex)
        {
            if (CanAcceptVideo() && gridIndex > _lastSubmittedPts
                && _videoIn.TryEnqueue(new VideoWork(VideoWorkKind.SharedSlot, 0, 0, slot, gridIndex)))
            {
                _lastSubmittedPts = gridIndex;
                Interlocked.Increment(ref _recordedVideoFrames);
                return true;
            }

            _ring?.Release(slot);
            Interlocked.Increment(ref _droppedFrames);
            return false;
        }

        /// <summary>
        /// Queues a repeat of the last picture at <paramref name="gridIndex"/> — no pixel data
        /// moves; the encoder just re-sends the picture it already has.
        /// </summary>
        public bool RepeatLastVideoFrame(long gridIndex)
        {
            // Nothing to repeat before the first picture (and the index must stay free for it).
            if (_lastSubmittedPts >= 0 && CanAcceptVideo() && gridIndex > _lastSubmittedPts
                && _videoIn.TryEnqueue(new VideoWork(VideoWorkKind.Repeat, 0, 0, 0, gridIndex)))
            {
                _lastSubmittedPts = gridIndex;
                Interlocked.Increment(ref _recordedVideoFrames);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Copying entry point: the frame is copied into a pooled buffer, so the caller may
        /// reuse its own memory immediately. The grid index is derived from the frame's QPC
        /// timestamp relative to the first frame pushed. (The live capture path uses the
        /// zero-copy hand-over methods above instead.)
        /// </summary>
        public void PushVideoFrame(in RawVideoFrame frame)
        {
            if (!CanAcceptVideo())
                return;

            if (_legacyT0 == long.MinValue)
                _legacyT0 = frame.TimestampQpc;

            long gridIndex = (long)Math.Round(
                (double)(frame.TimestampQpc - _legacyT0) * _videoParams.FpsNumerator / (_videoParams.FpsDenominator * (double)Stopwatch.Frequency));

            int frameBytes = frame.Stride * frame.Height + frame.Stride * (frame.Height / 2); // NV12
            nint buffer = _videoFramePool.Rent(frameBytes);
            Unsafe.CopyBlockUnaligned((void*)buffer, (void*)frame.Data, (uint)frameBytes);

            // No drop here: this path feeds deterministic content (tests, offline sources).
            Interlocked.Add(ref _pendingVideoBytes, frameBytes);
            if (gridIndex <= _lastSubmittedPts
                || !_videoIn.Enqueue(new VideoWork(VideoWorkKind.CpuFrame, buffer, frame.Stride, frameBytes, gridIndex)))
            {
                Interlocked.Add(ref _pendingVideoBytes, -frameBytes);
                _videoFramePool.Return(buffer);
                return;
            }

            _lastSubmittedPts = gridIndex;
            Interlocked.Increment(ref _recordedVideoFrames);
        }

        private bool CanAcceptVideo()
            => _started && !_stopped && _videoEncoder != null && Volatile.Read(ref _fault) == null;

        #endregion

        public void PushAudioFrame(in RawAudioFrame frame)
        {
            if (!_started || _stopped || _audioEncoder == null || Volatile.Read(ref _fault) != null)
                return;

            int bytesPerSample = frame.Format == SampleFormat.S16 ? 2 : frame.Format == SampleFormat.S32 || frame.Format == SampleFormat.FLT ? 4 : 1;
            int frameBytes = frame.Samples * frame.Channels * bytesPerSample;
            nint buffer = _audioFramePool.Rent(frameBytes);
            Unsafe.CopyBlockUnaligned((void*)buffer, (void*)frame.Data, (uint)frameBytes);

            if (!_audioIn.TryEnqueue(new AudioWork(buffer, frame.Samples)))
                _audioFramePool.Return(buffer);
        }

        private void VideoLoop()
        {
            while (_videoIn.TryDequeue(out var work))
            {
                bool usable = Volatile.Read(ref _fault) == null;
                bool prepared = false;

                try
                {
                    switch (work.Kind)
                    {
                        case VideoWorkKind.CpuFrame:
                            try
                            {
                                if (usable)
                                {
                                    _videoEncoder.PrepareNv12((byte*)work.Data, work.Stride, work.Pts);
                                    prepared = true;
                                }
                            }
                            finally
                            {
                                Interlocked.Add(ref _pendingVideoBytes, -work.Slot); // Slot carries the byte count
                                _videoFramePool.Return(work.Data);
                            }
                            break;

                        case VideoWorkKind.SharedSlot:
                            try
                            {
                                if (usable)
                                {
                                    _ring.BeginRead(work.Slot);
                                    prepared = _videoEncoder.PrepareShared(work.Slot, work.Pts);
                                }
                            }
                            finally
                            {
                                _ring.Release(work.Slot);
                            }
                            break;

                        case VideoWorkKind.Repeat:
                            prepared = usable && _videoEncoder.PrepareRepeat(work.Pts);
                            break;
                    }

                    if (!prepared)
                    {
                        if (usable)
                            Interlocked.Increment(ref _droppedFrames);
                        continue;
                    }

                    if (!_videoEncoder.TrySendPrepared())
                    {
                        DrainVideo();
                        if (!_videoEncoder.TrySendPrepared())
                            throw new InvalidOperationException("The video encoder refused input after being drained.");
                    }

                    DrainVideo();
                    Interlocked.Increment(ref _encodedVideoFrames);
                }
                catch (Exception ex)
                {
                    SetFault(ex);
                }
            }

            try
            {
                if (Volatile.Read(ref _fault) == null)
                {
                    _videoEncoder.FlushSend();
                    DrainVideo();
                }
            }
            catch (Exception ex)
            {
                SetFault(ex);
            }
        }

        private void DrainVideo()
        {
            while (_videoEncoder.TryReceive(out var p))
            {
                nint buffer = _packetPool.Rent(p.Size);
                Unsafe.CopyBlockUnaligned((void*)buffer, (void*)p.Data, (uint)p.Size);
                if (!_encodedOut.Enqueue(new QueuedPacket(true, p.PresentationTimeStamp, p.DecodingTimeStamp, buffer, p.Size, p.IsKeyFrame)))
                    _packetPool.Return(buffer);
            }
        }

        private void AudioLoop()
        {
            byte** planes = stackalloc byte*[1];

            while (_audioIn.TryDequeue(out var work))
            {
                try
                {
                    if (Volatile.Read(ref _fault) != null)
                        continue;

                    planes[0] = (byte*)work.Data;
                    _audioEncoder.Accept(planes, 1, work.Samples);

                    while (_audioEncoder.TrySendNextFrame())
                        DrainAudio();
                }
                catch (Exception ex)
                {
                    SetFault(ex);
                }
                finally
                {
                    _audioFramePool.Return(work.Data);
                }
            }

            try
            {
                if (Volatile.Read(ref _fault) == null)
                {
                    _audioEncoder.Flush();
                    DrainAudio();
                }
            }
            catch (Exception ex)
            {
                SetFault(ex);
            }
        }

        private void DrainAudio()
        {
            EncodedAudioPacket? packet;
            while ((packet = _audioEncoder.ReceivePacket()) != null)
            {
                var p = packet.Value;
                nint buffer = _packetPool.Rent(p.Size);
                Unsafe.CopyBlockUnaligned((void*)buffer, (void*)p.Data, (uint)p.Size);
                if (!_encodedOut.Enqueue(new QueuedPacket(false, p.PresentationTimeStamp, p.DecodingTimeStamp, buffer, p.Size, false)))
                    _packetPool.Return(buffer);
            }
        }

        private void MuxLoop()
        {
            while (_encodedOut.TryDequeue(out var q))
            {
                try
                {
                    // After a fault keep draining (so the encoders never block on a full queue)
                    // but stop touching the file.
                    if (Volatile.Read(ref _fault) != null)
                        continue;

                    if (q.IsVideo)
                        _container.WriteVideoPacket(new EncodedVideoPacket(q.Pts, q.Dts, q.Data, q.Size, q.IsKey));
                    else
                        _container.WriteAudioPacket(new EncodedAudioPacket(q.Pts, q.Dts, q.Data, q.Size));

                    Interlocked.Exchange(ref _bytesWritten, _container.BytesWritten);
                }
                catch (Exception ex)
                {
                    SetFault(ex);
                }
                finally
                {
                    _packetPool.Return(q.Data);
                }
            }
        }

        private void SetFault(Exception ex)
        {
            if (Interlocked.CompareExchange(ref _fault, ex, null) != null)
                return;

            // Never raise on the failing pipeline thread: the handler stops the recorder, which
            // joins that very thread.
            ThreadPool.UnsafeQueueUserWorkItem(static state =>
            {
                var recorder = (Recorder)state;
                try { recorder.Faulted?.Invoke(recorder.Fault); } catch { }
            }, this);
        }

        /// <summary>
        /// Drains the encoders and the muxer and finalizes the file. Returns when everything
        /// queued so far has been written (or abandoned after a fault).
        /// </summary>
        public void Stop()
        {
            if (!_started || _stopped)
                return;
            _stopped = true;

            _videoIn.Complete();
            _audioIn.Complete();

            _videoThread?.Join();
            _audioThread?.Join();

            _encodedOut.Complete();
            _muxThread?.Join();

            try
            {
                bool finalized = _container != null && _container.Complete();
                CompletedSuccessfully = finalized && Volatile.Read(ref _fault) == null;
                if (_container != null)
                {
                    IsEmpty = _container.PacketsWritten == 0;
                    Interlocked.Exchange(ref _bytesWritten, _container.BytesWritten); // the final file size
                }
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref _fault, ex, null);
            }

            DisposePipeline();
        }

        private void DisposePipeline()
        {
            try { _container?.Dispose(); } catch { }
            try { _videoEncoder?.Dispose(); } catch { }
            try { _audioEncoder?.Dispose(); } catch { }
            _container = null;
            _videoEncoder = null;
            _audioEncoder = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            Stop();

            // Pending work items (possible only if Start threw) still own pooled buffers; the
            // pools free every buffer they ever handed out.
            _videoFramePool.Dispose();
            _audioFramePool.Dispose();
            _packetPool.Dispose();
        }
    }
}
