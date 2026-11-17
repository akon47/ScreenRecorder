using System;
using System.IO;
using System.Threading;
using MediaEncoder;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.Gif
{
    /// <summary>
    /// Records captured BGRA frames to an animated GIF. It is the GIF counterpart of
    /// <see cref="Recorder"/> and takes frames through the same hand-over contract
    /// (<see cref="IVideoFrameSink"/>): the capture thread rents a buffer, fills it and submits
    /// it with its grid index; one encode thread quantizes, compresses and writes. The capture
    /// thread never blocks — when the encoder falls behind, frames are dropped and counted
    /// (a dropped frame just means the previous one stays on screen a little longer).
    /// Failures are contained and reported through <see cref="Faulted"/>, exactly like the
    /// video recorder.
    /// </summary>
    public sealed unsafe class GifRecorder : IVideoFrameSink, IDisposable
    {
        private readonly struct Work
        {
            public readonly nint Data;
            public readonly int Stride;
            public readonly int Bytes;
            public readonly long Index;

            public Work(nint data, int stride, int bytes, long index)
            {
                Data = data;
                Stride = stride;
                Bytes = bytes;
                Index = index;
            }
        }

        /// <summary>Upper bound on raw frames waiting for the encoder, by memory.</summary>
        private const long MaxPendingBytes = 96L * 1024 * 1024;

        private readonly string _path;
        private readonly int _width;
        private readonly int _height;
        private readonly int _fpsNumerator;
        private readonly int _fpsDenominator;

        private readonly WorkQueue<Work> _queue = new WorkQueue<Work>(32);
        private readonly UnmanagedBufferPool _pool = new UnmanagedBufferPool();
        private readonly int _maxPendingFrames;

        private FileStream _file;
        private GifEncoder _encoder;
        private Thread _thread;

        private Exception _fault;
        private long _pendingBytes;
        private long _droppedFrames;
        private long _submittedFrames;
        private long _bytesWritten;
        private long _lastSubmittedIndex = -1;
        private long _endIndex = -1;
        private bool _started;
        private bool _stopped;
        private bool _disposed;

        public GifRecorder(string path, int width, int height, int fpsNumerator, int fpsDenominator = 1)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (fpsNumerator <= 0 || fpsDenominator <= 0)
                throw new ArgumentOutOfRangeException(nameof(fpsNumerator));

            _width = width;
            _height = height;
            _fpsNumerator = fpsNumerator;
            _fpsDenominator = fpsDenominator;

            long frameBytes = (long)width * height * 4;
            _maxPendingFrames = (int)Math.Clamp(MaxPendingBytes / Math.Max(1, frameBytes), 2, 8);
        }

        /// <summary>Raised once, on a thread-pool thread, when encoding or writing fails.</summary>
        public event Action<Exception> Faulted;

        public Exception Fault => Volatile.Read(ref _fault);

        public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

        /// <summary>Frames accepted for encoding so far.</summary>
        public long RecordedVideoFrames => Interlocked.Read(ref _submittedFrames);

        public long BytesWritten => Interlocked.Read(ref _bytesWritten);

        /// <summary>True when the file was finalized without error (valid after Stop).</summary>
        public bool CompletedSuccessfully { get; private set; }

        public void Start()
        {
            if (_started)
                return;
            _started = true;

            _file = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            _encoder = new GifEncoder(_file, _width, _height);

            _thread = new Thread(EncodeLoop) { Name = "GifEncode", IsBackground = true };
            _thread.Start();
        }

        #region IVideoFrameSink

        public nint RentVideoBuffer(int bytes)
        {
            if (!CanAccept() || Volatile.Read(ref _pendingBytes) >= (long)_maxPendingFrames * bytes)
            {
                Interlocked.Increment(ref _droppedFrames);
                return 0;
            }

            return _pool.Rent(bytes);
        }

        public void ReturnVideoBuffer(nint buffer) => _pool.Return(buffer);

        public bool SubmitVideoBuffer(nint buffer, int stride, int bytes, long gridIndex)
        {
            if (!CanAccept() || gridIndex <= _lastSubmittedIndex)
            {
                _pool.Return(buffer);
                Interlocked.Increment(ref _droppedFrames);
                return false;
            }

            Interlocked.Add(ref _pendingBytes, bytes);
            if (!_queue.TryEnqueue(new Work(buffer, stride, bytes, gridIndex)))
            {
                Interlocked.Add(ref _pendingBytes, -bytes);
                _pool.Return(buffer);
                Interlocked.Increment(ref _droppedFrames);
                return false;
            }

            _lastSubmittedIndex = gridIndex;
            Interlocked.Increment(ref _submittedFrames);
            return true;
        }

        /// <summary>
        /// A GIF has no "repeat" frame: showing the last picture longer is just a later end
        /// time. The capture source calls this once at stop with the final grid index.
        /// </summary>
        public bool RepeatLastVideoFrame(long gridIndex)
        {
            if (!_started || _lastSubmittedIndex < 0 || gridIndex <= _lastSubmittedIndex)
                return false;

            Interlocked.Exchange(ref _endIndex, gridIndex);
            return true;
        }

        #endregion

        private bool CanAccept() => _started && !_stopped && Volatile.Read(ref _fault) == null;

        /// <summary>Grid index → presentation time in GIF's unit, 1/100 s.</summary>
        internal long ToCentiseconds(long gridIndex)
            => gridIndex * 100L * _fpsDenominator / _fpsNumerator;

        private void EncodeLoop()
        {
            while (_queue.TryDequeue(out Work work))
            {
                try
                {
                    if (Volatile.Read(ref _fault) == null)
                    {
                        _encoder.AddFrame((byte*)work.Data, work.Stride, ToCentiseconds(work.Index));
                        Interlocked.Exchange(ref _bytesWritten, _file.Position);
                    }
                }
                catch (Exception ex)
                {
                    SetFault(ex);
                }
                finally
                {
                    Interlocked.Add(ref _pendingBytes, -work.Bytes);
                    _pool.Return(work.Data);
                }
            }
        }

        private void SetFault(Exception ex)
        {
            if (Interlocked.CompareExchange(ref _fault, ex, null) != null)
                return;

            ThreadPool.UnsafeQueueUserWorkItem(static state =>
            {
                var recorder = (GifRecorder)state;
                try { recorder.Faulted?.Invoke(recorder.Fault); } catch { }
            }, this);
        }

        /// <summary>Encodes what is still queued, writes the last frame and closes the file.</summary>
        public void Stop()
        {
            if (!_started || _stopped)
                return;
            _stopped = true;

            _queue.Complete();
            _thread?.Join();

            try
            {
                if (Volatile.Read(ref _fault) == null)
                {
                    // The last picture stays up until the stop position, and at least one frame period.
                    long end = Math.Max(Interlocked.Read(ref _endIndex), _lastSubmittedIndex + 1);
                    _encoder.Finish(ToCentiseconds(end));
                    _file.Flush();
                    Interlocked.Exchange(ref _bytesWritten, _file.Position);
                    CompletedSuccessfully = _lastSubmittedIndex >= 0;
                }
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref _fault, ex, null);
            }
            finally
            {
                try { _encoder?.Dispose(); } catch { }
                try { _file?.Dispose(); } catch { }
                _encoder = null;
                _file = null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            Stop();
            _pool.Dispose();
        }
    }
}
