using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using ScreenRecorder.DirectX;
using ScreenRecorder.Encoder;
using Vortice;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Rect = System.Windows.Rect;

namespace ScreenRecorder.VideoSource
{
    /// <summary>
    /// One thing to capture and where it goes in the recorded picture. A recording normally has
    /// a single layer; an area that spans several monitors has one layer per monitor.
    /// </summary>
    internal sealed class CaptureLayer
    {
        /// <summary>The monitor or window to capture.</summary>
        public GraphicsCaptureItem Item;

        /// <summary>Part of the item to take (item-relative, physical pixels); Empty = all of it (window capture).</summary>
        public Rect Source = Rect.Empty;

        /// <summary>Rectangle of the output picture this layer fills (output pixels).</summary>
        public RawRect Area;
    }

    /// <summary>Output pixel layout of the frames a <see cref="ScreenVideoSource"/> produces.</summary>
    internal enum CaptureOutputFormat
    {
        /// <summary>NV12 — for the video encoders.</summary>
        Nv12,

        /// <summary>BGRA — for the GIF encoder.</summary>
        Bgra,
    }

    /// <summary>
    /// The screen/window capture source: owns the WGC captures, the GPU converters and the
    /// paced grid clock, and feeds its sink one decision per grid tick (see
    /// <see cref="FrameEmissionPolicy"/>): encode the newly captured picture, repeat the last
    /// one, or — in variable-frame-rate mode with nothing changed — do nothing at all.
    ///
    /// Two conversion paths, chosen by whether a shared GPU ring is supplied:
    /// <list type="bullet">
    /// <item><b>GPU</b>: the converters write NV12 straight into a shared ring texture that the
    /// encoder's own device reads. No pixel ever touches the CPU.</item>
    /// <item><b>CPU</b>: converters → staging texture → one copy into a pooled buffer that the
    /// sink takes ownership of. The readback is pipelined by one tick so the capture thread
    /// never waits for the GPU; the frame keeps the PTS of the tick it was captured on.</item>
    /// </list>
    /// When the screen is static the tick does nothing but compare a counter per layer.
    /// </summary>
    public sealed unsafe class ScreenVideoSource : IDisposable
    {
        private const long Unconsumed = -1;

        /// <summary>One layer's live state.</summary>
        private sealed class LayerState
        {
            public CaptureLayer Layer;
            public WgcCapture Capture;
            public FrameConverter Converter;
            public long ConsumedSerial;
            public int PoolGeneration;
        }

        private readonly IVideoFrameSink _sink;
        private readonly Recorder _gpuRecorder;   // set together with _ring (zero-copy path)
        private readonly SharedNv12Ring _ring;
        private readonly Direct3D11Device _device;
        private readonly LayerState[] _layers;
        private readonly FrameEmissionPolicy _policy;
        private readonly int _fpsNumerator;
        private readonly int _fpsDenominator;

        private PacedClock _clock;
        private Thread _thread;
        private CancellationTokenSource _cts;

        private int _stagingSlot;
        private int _pendingSlot = -1;
        private long _pendingIndex;

        private long _newFrames;
        private long _repeatedFrames;
        private int _faulted;
        private bool _stopped;
        private bool _disposed;

        /// <summary>A capture target went away (window closed / display disconnected).</summary>
        public event Action TargetClosed;

        /// <summary>The capture thread failed (e.g. graphics device removed). Raised once.</summary>
        public event Action<Exception> Faulted;

        /// <param name="sink">Receives CPU frames (and repeats).</param>
        /// <param name="gpuRecorder">With <paramref name="ring"/>: the recorder that takes ring slots instead of CPU frames.</param>
        /// <param name="keepAliveSeconds">
        /// Variable frame rate only: with nothing changing, still emit one repeat this often
        /// (bounds sample durations in video files). 0 = never (GIF: a still picture is just a
        /// longer delay).
        /// </param>
        internal ScreenVideoSource(
            Direct3D11Device device, IReadOnlyList<CaptureLayer> layers, int outWidth, int outHeight, CaptureOutputFormat outputFormat,
            bool drawCursor, bool showBorder,
            int fpsNumerator, int fpsDenominator, bool variableFrameRate, double keepAliveSeconds,
            IVideoFrameSink sink, Recorder gpuRecorder = null, SharedNv12Ring ring = null)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            if (layers == null || layers.Count == 0)
                throw new ArgumentException("At least one capture layer is required.", nameof(layers));
            if (ring != null && gpuRecorder == null)
                throw new ArgumentNullException(nameof(gpuRecorder));

            _ring = ring;
            _gpuRecorder = ring != null ? gpuRecorder : null;
            _fpsNumerator = fpsNumerator;
            _fpsDenominator = fpsDenominator;

            double fps = (double)fpsNumerator / fpsDenominator;
            long keepAliveTicks = keepAliveSeconds > 0 ? Math.Max(1, (long)Math.Round(fps * keepAliveSeconds)) : long.MaxValue;
            _policy = new FrameEmissionPolicy(variableFrameRate, keepAliveTicks);

            Format format = outputFormat == CaptureOutputFormat.Bgra ? Format.B8G8R8A8_UNorm : Format.NV12;
            _layers = new LayerState[layers.Count];
            try
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    var state = new LayerState { Layer = layers[i] };
                    _layers[i] = state;
                    state.Capture = new WgcCapture(device, layers[i].Item, drawCursor, showBorder);
                    state.Capture.Closed += OnCaptureClosed;
                    state.Converter = new FrameConverter(device.Device, device.Context, outWidth, outHeight, format);
                }
            }
            catch
            {
                DisposeLayers();
                throw;
            }
        }

        /// <summary>Grid ticks skipped because the capture thread woke late.</summary>
        public long LateTicks => _clock?.LateTicks ?? 0;

        /// <summary>Pictures actually converted and handed to the encoder.</summary>
        public long NewFrames => Interlocked.Read(ref _newFrames);

        /// <summary>Repeats of the previous picture (CFR fill / VFR keep-alive).</summary>
        public long RepeatedFrames => Interlocked.Read(ref _repeatedFrames);

        /// <summary>The grid index the clock is at (elapsed recording time × fps).</summary>
        public long CurrentGridIndex => _clock?.CurrentIndex ?? -1;

        public bool IsVariableFrameRate => _policy.IsVariableFrameRate;

        public void Pause() => _clock?.Pause();

        public void Resume() => _clock?.Resume();

        /// <summary>
        /// Starts capturing. <paramref name="t0"/> is the recording origin (QPC ticks) shared
        /// with the audio source: grid index 0 sits exactly there.
        /// </summary>
        public void Start(long t0)
        {
            if (_clock != null)
                return;

            _clock = new PacedClock(_fpsNumerator, _fpsDenominator, t0, OnTick);
            foreach (LayerState layer in _layers)
                layer.Capture.Start();

            _cts = new CancellationTokenSource();
            _thread = new Thread(Run)
            {
                Name = "ScreenCaptureClock",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();
        }

        private void Run()
        {
            try
            {
                _clock.Run(_cts.Token);
            }
            catch (Exception ex)
            {
                OnFault(ex);
            }
        }

        private void OnTick(long k, int missed)
        {
            try
            {
                CompletePendingReadback();

                // Constant frame rate: indices skipped by a late wake-up still get a frame.
                if (missed > 0 && _policy.FillsMissedTicks)
                {
                    for (long i = k - missed; i < k; i++)
                        Repeat(i);
                }

                FrameAction action = _policy.Decide(k, HasNewContent());

                if (action == FrameAction.EncodeNew && !EncodeNew(k))
                    action = _policy.Decide(k, hasNewContent: false);

                if (action == FrameAction.RepeatLast)
                    Repeat(k);
            }
            catch (Exception ex)
            {
                OnFault(ex);
            }
        }

        /// <summary>
        /// True when any layer has a frame the last picture did not include — but only once
        /// EVERY layer has delivered its first frame, so a picture is never composed with a
        /// blank part.
        /// </summary>
        private bool HasNewContent()
        {
            bool changed = false;
            foreach (LayerState layer in _layers)
            {
                long serial = layer.Capture.FrameSerial;
                if (serial == 0)
                    return false;

                if (serial != layer.ConsumedSerial)
                    changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Converts the latest captured picture for grid index <paramref name="k"/>. Returns
        /// false when nothing was started (no frame yet, or the encoder is behind).
        /// </summary>
        private bool EncodeNew(long k)
        {
            if (_ring != null)
                return EncodeNewGpu(k);

            if (_pendingSlot >= 0)
                return false; // a readback is still outstanding (cannot happen: completed at tick start)

            FrameConverter primary = _layers[0].Converter;
            if (!ComposeInto(primary.CpuTarget))
                return false;

            primary.QueueReadback(_stagingSlot);
            _pendingSlot = _stagingSlot;
            _pendingIndex = k;
            _stagingSlot = (_stagingSlot + 1) % primary.StagingSlots;
            return true;
        }

        private bool EncodeNewGpu(long k)
        {
            int slot = _ring.TryBeginWrite();
            if (slot < 0)
            {
                // Every slot is still in flight: the encoder is behind. Leave the content
                // unconsumed so a later tick picks it up.
                return false;
            }

            bool written = false;
            try
            {
                written = ComposeInto(_ring.GetTexture(slot));

                // Submit the conversion before the encoder's device can take the mutex.
                if (written)
                    _device.Context.Flush();
            }
            finally
            {
                _ring.EndWrite(slot, queued: written);
            }

            if (!written)
                return false;

            long pts = _policy.ToPresentationIndex(k);
            if (!_gpuRecorder.SubmitSharedSlot(slot, pts))
            {
                MarkUnconsumed(); // dropped: convert this content again next tick
                return false;
            }

            OnPictureEmitted(k);
            return true;
        }

        /// <summary>
        /// Draws every layer's latest frame into <paramref name="destination"/>. All layers are
        /// drawn each time (destination textures rotate, so nothing can be assumed to be there
        /// already). Returns false when a layer has no frame.
        /// </summary>
        private bool ComposeInto(ID3D11Texture2D destination)
        {
            for (int i = 0; i < _layers.Length; i++)
            {
                LayerState layer = _layers[i];

                int generation = layer.Capture.PoolGeneration;
                if (generation != layer.PoolGeneration)
                {
                    // The frame pool was rebuilt (resize): drop views onto the old textures.
                    layer.PoolGeneration = generation;
                    layer.Converter.ClearInputViews();
                }

                if (!layer.Capture.TryLockLatest(out nint texture, out int width, out int height, out long serial))
                    return false;

                try
                {
                    layer.ConsumedSerial = serial;
                    RawRect source = SourceRect(layer.Layer, width, height);
                    layer.Converter.Blit(texture, width, height, source, layer.Layer.Area, fillOutput: i == 0, destination);
                }
                finally
                {
                    layer.Capture.UnlockLatest();
                }
            }

            return true;
        }

        /// <summary>
        /// CPU path: reads back the frame converted on the previous tick and hands it to the
        /// sink with the PTS of the tick it was captured on.
        /// </summary>
        private void CompletePendingReadback()
        {
            if (_pendingSlot < 0)
                return;

            int slot = _pendingSlot;
            long k = _pendingIndex;
            _pendingSlot = -1;

            FrameConverter primary = _layers[0].Converter;
            primary.MapStaging(slot, out nint data, out int rowPitch);
            try
            {
                int bytes = primary.GetFrameBytes(rowPitch);
                nint buffer = _sink.RentVideoBuffer(bytes);
                if (buffer == 0)
                {
                    // Sink behind → frame dropped (counted by the sink). Convert the content
                    // again next tick, or a dropped last change would never be recorded.
                    MarkUnconsumed();
                    return;
                }

                Unsafe.CopyBlockUnaligned((void*)buffer, (void*)data, (uint)bytes);

                long pts = _policy.ToPresentationIndex(k);
                if (_sink.SubmitVideoBuffer(buffer, rowPitch, bytes, pts))
                    OnPictureEmitted(k);
                else
                    MarkUnconsumed();
            }
            finally
            {
                primary.UnmapStaging(slot);
            }
        }

        private void MarkUnconsumed()
        {
            foreach (LayerState layer in _layers)
                layer.ConsumedSerial = Unconsumed;
        }

        private void OnPictureEmitted(long k)
        {
            bool first = !_policy.HasFrame;
            Interlocked.Increment(ref _newFrames);

            if (first)
            {
                // The first picture is stamped 0 so video starts with audio. For constant frame
                // rate, fill the indices up to where it was really captured.
                _policy.MarkEmitted(0);
                if (!_policy.IsVariableFrameRate)
                {
                    for (long i = 1; i <= k; i++)
                        Repeat(i);
                }
            }

            _policy.MarkEmitted(k);
        }

        private void Repeat(long index)
        {
            if (_sink.RepeatLastVideoFrame(index))
            {
                _policy.MarkEmitted(index);
                Interlocked.Increment(ref _repeatedFrames);
            }
        }

        private static RawRect SourceRect(CaptureLayer layer, int contentWidth, int contentHeight)
        {
            if (layer.Source.IsEmpty)
                return new RawRect(0, 0, Math.Max(2, contentWidth & ~1), Math.Max(2, contentHeight & ~1));

            int left = Math.Clamp((int)layer.Source.Left, 0, Math.Max(0, contentWidth - 2));
            int top = Math.Clamp((int)layer.Source.Top, 0, Math.Max(0, contentHeight - 2));
            int right = Math.Min(contentWidth, left + (int)layer.Source.Width);
            int bottom = Math.Min(contentHeight, top + (int)layer.Source.Height);
            return new RawRect(left, top, right, bottom);
        }

        private void OnCaptureClosed()
        {
            try { TargetClosed?.Invoke(); } catch { }
        }

        private void OnFault(Exception ex)
        {
            if (Interlocked.Exchange(ref _faulted, 1) != 0)
                return;

            _cts?.Cancel();
            ThreadPool.UnsafeQueueUserWorkItem(static state =>
            {
                var (source, error) = ((ScreenVideoSource, Exception))state;
                try { source.Faulted?.Invoke(error); } catch { }
            }, (this, ex));
        }

        /// <summary>
        /// Stops the capture clock and emits the closing frame(s): the outstanding readback and,
        /// if the picture has not changed for a while, one last repeat at the stop position so
        /// the recording is as long as the time that passed.
        /// </summary>
        public void Stop()
        {
            if (_stopped)
                return;
            _stopped = true;

            _cts?.Cancel();
            bool joined = _thread == null || _thread.Join(3000);
            _thread = null;

            if (!joined || _clock == null || Volatile.Read(ref _faulted) != 0)
                return;

            try
            {
                CompletePendingReadback();

                long k = _clock.CurrentIndex;
                if (_policy.NeedsFinalFrame(k))
                    Repeat(k);
            }
            catch
            {
                // Best effort: the recording is closing anyway.
            }
        }

        private void DisposeLayers()
        {
            foreach (LayerState layer in _layers)
            {
                if (layer == null)
                    continue;

                if (layer.Capture != null)
                {
                    layer.Capture.Closed -= OnCaptureClosed;
                    try { layer.Capture.Dispose(); } catch { }
                }

                try { layer.Converter?.Dispose(); } catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            Stop();
            DisposeLayers();
            _cts?.Dispose();
        }
    }
}
