using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Windows;
using MediaEncoder;
using ScreenRecorder.AudioSource;
using ScreenRecorder.DirectX;
using ScreenRecorder.Gif;
using ScreenRecorder.VideoSource;
using Vortice;
using Windows.Graphics.Capture;

namespace ScreenRecorder.Encoder
{
    public sealed class RecordingStoppedEventArgs : EventArgs
    {
        /// <summary>Path of the file that was being written.</summary>
        public string Url { get; }

        /// <summary>True when the file was finalized without error.</summary>
        public bool Completed { get; }

        public RecordingStoppedEventArgs(string url, bool completed)
        {
            Url = url;
            Completed = completed;
        }
    }

    /// <summary>
    /// The engine's front door: one recording at a time, described by
    /// <see cref="RecordingOptions"/>. It assembles the pipeline for that recording —
    /// <list type="bullet">
    /// <item>video file: WGC capture (<see cref="ScreenVideoSource"/>) + WASAPI audio
    /// (<see cref="WasapiAudioSource"/>) → <see cref="Recorder"/> (FFmpeg encode + mux), both
    /// sources sharing one QPC origin so sound and picture line up;</item>
    /// <item>audio file: the audio source alone → <see cref="Recorder"/>;</item>
    /// <item>GIF: WGC capture → <see cref="GifRecorder"/>;</item>
    /// </list>
    /// — and tears it down again on <see cref="Stop"/>, finalizing the file. Pausing excludes
    /// the paused time from the recording. All public members are thread-safe.
    /// </summary>
    public sealed class ScreenEncoder : IDisposable
    {
        private readonly object _lifecycleLock = new object();
        private readonly RecordingClock _clock = new RecordingClock();

        private volatile Recorder _recorder;
        private volatile GifRecorder _gif;
        private volatile ScreenVideoSource _video;
        private WasapiAudioSource _audio;
        private Direct3D11Device _device;
        private SharedNv12Ring _ring;
        private string _url;
        private GCLatencyMode _previousLatencyMode;
        private bool _latencyModeChanged;
        private bool _powerHeld;
        private int _interruptRaised;
        private volatile EncoderStatus _status = EncoderStatus.Stop;

        /// <summary>
        /// Raised inside <see cref="Start"/>, after the pipeline is built but before the first
        /// frame is captured — the moment for the app to hide its own windows from capture.
        /// </summary>
        public event EventHandler CaptureStarting;

        /// <summary>Raised after a recording was stopped and its file closed.</summary>
        public event EventHandler<RecordingStoppedEventArgs> Stopped;

        /// <summary>
        /// Raised (on a thread-pool thread) when the recording cannot continue: the recorded
        /// window was closed, writing failed, or capture failed. The recording is still
        /// "running" at that point — the subscriber decides when to call <see cref="Stop"/>
        /// (the app does it on the UI thread and tells the user why).
        /// </summary>
        public event EventHandler<RecordingInterruptedEventArgs> Interrupted;

        public EncoderStatus Status => _status;

        /// <summary>True from Start until Stop (paused counts as running).</summary>
        public bool IsRunning => _status != EncoderStatus.Stop;

        /// <summary>Recorded time, excluding paused time. Zero when not recording.</summary>
        public TimeSpan Elapsed => _clock.Elapsed;

        /// <summary>True when the last Stop finalized its file without error.</summary>
        public bool LastRecordingCompleted { get; private set; }

        /// <summary>The hardware backend of the current/last video recording (after any fallback).</summary>
        public HwAccel ActiveHwAccel { get; private set; } = HwAccel.Software;

        /// <summary>FFmpeg encoder of the current/last video recording (e.g. <c>h264_nvenc</c>), or null.</summary>
        public string VideoEncoderName { get; private set; }

        /// <summary>True when the current/last recording fed the encoder GPU textures directly.</summary>
        public bool UsesGpuInput { get; private set; }

        /// <summary>True when the current/last recording was variable frame rate ("auto").</summary>
        public bool IsVariableFrameRate { get; private set; }

        /// <summary>Capture grid rate (fps) of the current/last recording; 0 for audio-only.</summary>
        public int GridFrameRate { get; private set; }

        /// <summary>Output picture size of the current/last recording; 0 for audio-only.</summary>
        public int VideoWidth { get; private set; }

        public int VideoHeight { get; private set; }

        /// <summary>
        /// Video frames accepted for encoding so far. In automatic frame-rate mode this only
        /// advances while the screen changes — use <see cref="Elapsed"/> for the time.
        /// </summary>
        public long LiveVideoFrames => _recorder?.RecordedVideoFrames ?? _gif?.RecordedVideoFrames ?? 0;

        /// <summary>True while the running recording has a sound track.</summary>
        public bool HasAudio => _audio != null;

        /// <summary>
        /// The loudest level that went into the sound track since the previous call, 0..1;
        /// 0 when nothing is being recorded. Cheap enough to call every display frame.
        /// </summary>
        public float ReadAudioPeak() => _audio?.ReadPeak() ?? 0f;

        public RecordingStatistics GetStatistics()
        {
            Recorder recorder = _recorder;
            GifRecorder gif = _gif;
            return new RecordingStatistics(
                _clock.Elapsed,
                recorder?.BytesWritten ?? gif?.BytesWritten ?? 0,
                recorder?.RecordedVideoFrames ?? gif?.RecordedVideoFrames ?? 0,
                recorder?.DroppedFrames ?? gif?.DroppedFrames ?? 0,
                _video?.LateTicks ?? 0);
        }

        #region Start

        /// <summary>
        /// Starts recording. Throws when the recording cannot be set up (nothing is left
        /// running in that case). Does nothing while a recording is already running.
        /// </summary>
        public void Start(RecordingOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.Url))
                throw new ArgumentException("An output path is required.", nameof(options));

            lock (_lifecycleLock)
            {
                if (IsRunning)
                    return;

                Interlocked.Exchange(ref _interruptRaised, 0);
                LastRecordingCompleted = false;
                _url = options.Url;

                try
                {
                    if (options.IsGif)
                        BuildGifPipeline(options);
                    else
                        BuildVideoPipeline(options);

                    HoldPower();

                    _status = EncoderStatus.Start;
                    CaptureStarting?.Invoke(this, EventArgs.Empty);

                    // One origin for both streams, taken only now: everything slow (encoder and
                    // device setup) is behind us, so neither stream starts with a backlog.
                    long t0 = Stopwatch.GetTimestamp();
                    _clock.Start(t0);
                    _video?.Start(t0);
                    _audio?.Start(t0);
                }
                catch
                {
                    _status = EncoderStatus.Stop;
                    TearDown();
                    throw;
                }
            }
        }

        private void BuildVideoPipeline(RecordingOptions o)
        {
            AudioParams audioParams = !o.HasAudio ? null : new AudioParams
            {
                Codec = o.AudioCodec,
                SampleRate = 48000,
                Channels = 2,
                Bitrate = o.AudioBitrate,
                Format = SampleFormat.S16,
            };

            if (o.AudioOnly && audioParams == null)
                throw new InvalidOperationException("Audio-only recording needs an audio codec and at least one audio source.");

            VideoParams videoParams = null;
            VideoTarget target = null;
            bool variable = false;
            int gridFps = 0;

            if (!o.AudioOnly)
            {
                target = ResolveVideoTarget(o);
                variable = AutoFrameRate.IsAuto(o.FrameRate);
                gridFps = Math.Max(1, AutoFrameRate.ResolveGridRate(o.FrameRate, target.RefreshRate));

                videoParams = new VideoParams
                {
                    Codec = o.VideoCodec,
                    Hw = HardwareEncoders.GetBest(o.VideoCodec, o.RateControl),
                    Width = target.Width,
                    Height = target.Height,
                    FpsNumerator = gridFps,
                    FpsDenominator = 1,
                    Bitrate = o.VideoBitrate,
                    RateControl = o.RateControl,
                    Quality = o.Quality,
                    VariableFrameRate = variable,
                };

                _device = new Direct3D11Device();

                // Zero-copy path: NVENC reads the converted frames straight from GPU textures.
                // Any failure here (or later in the encoder) quietly falls back to CPU frames.
                if (videoParams.Hw == HwAccel.Nvenc)
                {
                    try { _ring = new SharedNv12Ring(_device.Device, target.Width, target.Height); }
                    catch { _ring = null; }
                    videoParams.SharedRing = _ring;
                }
            }

            var recorder = new Recorder(o.Url, o.Format, videoParams, audioParams);
            _recorder = recorder;
            recorder.Faulted += OnWriteFaulted;
            recorder.Start();

            if (_ring != null && !recorder.UsesGpuInput)
            {
                _ring.Dispose();
                _ring = null;
            }

            ActiveHwAccel = videoParams != null ? recorder.ActualHwAccel : HwAccel.Software;
            VideoEncoderName = recorder.VideoEncoderName;
            UsesGpuInput = recorder.UsesGpuInput;
            IsVariableFrameRate = variable;
            GridFrameRate = gridFps;
            VideoWidth = target?.Width ?? 0;
            VideoHeight = target?.Height ?? 0;

            if (target != null)
            {
                // Variable rate: with a still screen, repeat the picture once a second so
                // sample durations stay bounded (seeking, editors, crash recovery).
                AttachVideoSource(new ScreenVideoSource(
                    _device, target.Layers, target.Width, target.Height, CaptureOutputFormat.Nv12,
                    o.DrawCursor, showBorder: false,
                    gridFps, 1, variable, keepAliveSeconds: 1.0,
                    recorder, recorder, _ring));
            }

            if (audioParams != null)
            {
                _audio = new WasapiAudioSource(new AudioSourceOptions
                {
                    CaptureSystemAudio = o.RecordSystemAudio,
                    SystemAudioDeviceId = o.SystemAudioDeviceId,
                    CaptureMicrophone = o.RecordMicrophone,
                    MicrophoneDeviceId = o.MicrophoneDeviceId,
                }, recorder);
            }
        }

        private void BuildGifPipeline(RecordingOptions o)
        {
            VideoTarget target = ResolveVideoTarget(o);
            int fps = Math.Clamp(o.FrameRate > 0 ? o.FrameRate : RecordingOptions.DefaultGifFrameRate, 1, 50);

            _device = new Direct3D11Device();

            var gif = new GifRecorder(o.Url, target.Width, target.Height, fps);
            _gif = gif;
            gif.Faulted += OnWriteFaulted;
            gif.Start();

            ActiveHwAccel = HwAccel.Software;
            VideoEncoderName = null;
            UsesGpuInput = false;
            IsVariableFrameRate = true;
            GridFrameRate = fps;
            VideoWidth = target.Width;
            VideoHeight = target.Height;

            // A GIF stores only pictures that differ: no keep-alive repeats.
            AttachVideoSource(new ScreenVideoSource(
                _device, target.Layers, target.Width, target.Height, CaptureOutputFormat.Bgra,
                o.DrawCursor, showBorder: false,
                fps, 1, variableFrameRate: true, keepAliveSeconds: 0,
                gif));
        }

        private void AttachVideoSource(ScreenVideoSource video)
        {
            video.TargetClosed += OnTargetClosed;
            video.Faulted += OnCaptureFaulted;
            _video = video;
        }

        /// <summary>What to capture, resolved to capture items and their place in the picture.</summary>
        private sealed class VideoTarget
        {
            public List<CaptureLayer> Layers;
            public int Width;
            public int Height;
            public int RefreshRate;
        }

        private static VideoTarget ResolveVideoTarget(RecordingOptions o)
        {
            double scale = o.OutputScale > 0 ? o.OutputScale : 1.0;

            if (o.WindowHandle != IntPtr.Zero)
            {
                // True window capture: WGC item sizes are physical pixels; the output size is
                // fixed at the window's size now (later resizes are scaled to fit).
                GraphicsCaptureItem item = WgcInterop.CreateItemForWindow(o.WindowHandle);
                int width = Math.Max(2, (int)Math.Round(item.Size.Width * scale) & ~1);
                int height = Math.Max(2, (int)Math.Round(item.Size.Height * scale) & ~1);

                return new VideoTarget
                {
                    Layers = new List<CaptureLayer> { new CaptureLayer { Item = item, Area = new RawRect(0, 0, width, height) } },
                    Width = width,
                    Height = height,
                    RefreshRate = MonitorInfo.FromWindow(o.WindowHandle)?.RefreshRate ?? 0,
                };
            }

            MonitorInfo[] monitors = MonitorInfo.GetActiveMonitorInfos();
            CaptureLayout layout;
            if (!o.DesktopRegion.IsEmpty)
            {
                layout = CaptureLayout.ForDesktopRegion(monitors, o.DesktopRegion);
            }
            else
            {
                MonitorInfo monitor = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, o.DeviceName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"Display '{o.DeviceName}' is not connected.");
                layout = CaptureLayout.ForMonitorRegion(monitor, o.Region);
            }

            if (layout == null)
                throw new ArgumentException("The capture region is outside the displays.");

            layout = layout.Scaled(scale);

            var layers = new List<CaptureLayer>(layout.Parts.Count);
            int refreshRate = 0;
            foreach (CaptureLayoutPart part in layout.Parts)
            {
                Rect d = part.Destination;
                layers.Add(new CaptureLayer
                {
                    Item = WgcInterop.CreateItemForMonitor(DisplayHelper.GetMonitorHandleFromDeviceName(part.Monitor.DeviceName)),
                    Source = part.Source,
                    Area = new RawRect((int)d.Left, (int)d.Top, (int)d.Right, (int)d.Bottom),
                });
                refreshRate = Math.Max(refreshRate, part.Monitor.RefreshRate);
            }

            return new VideoTarget { Layers = layers, Width = layout.Width, Height = layout.Height, RefreshRate = refreshRate };
        }

        private void HoldPower()
        {
            PowerHelper.PreventSleep();
            PowerHelper.SetThrottlingDisabled(true);
            _powerHeld = true;

            // A blocking gen-2 collection in the middle of a recording would stall the
            // capture/encode threads; prefer background collections while recording.
            try
            {
                _previousLatencyMode = GCSettings.LatencyMode;
                GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
                _latencyModeChanged = true;
            }
            catch
            {
                _latencyModeChanged = false;
            }
        }

        #endregion

        #region Pause / resume / stop

        public void Pause()
        {
            lock (_lifecycleLock)
            {
                if (_status != EncoderStatus.Start)
                    return;

                _status = EncoderStatus.Pause;
                _clock.Pause();
                _video?.Pause();
                _audio?.Pause();
            }
        }

        public void Resume()
        {
            lock (_lifecycleLock)
            {
                if (_status != EncoderStatus.Pause)
                    return;

                _status = EncoderStatus.Start;
                _clock.Resume();
                _video?.Resume();
                _audio?.Resume();
            }
        }

        /// <summary>
        /// Stops the recording: drains the encoders, finalizes and closes the file, then raises
        /// <see cref="Stopped"/>. Returns when the file is complete.
        /// </summary>
        public void Stop()
        {
            RecordingStoppedEventArgs args;
            lock (_lifecycleLock)
            {
                if (!IsRunning)
                    return;

                _status = EncoderStatus.Stop;
                TearDown();
                args = new RecordingStoppedEventArgs(_url, LastRecordingCompleted);
                _url = null;
            }

            Stopped?.Invoke(this, args);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch
            {
                // Still in use or read-only: leaving it is harmless.
            }
        }

        private void TearDown()
        {
            _clock.Reset();

            try { _audio?.Stop(); } catch { }
            try { _audio?.Dispose(); } catch { }
            _audio = null;

            ScreenVideoSource video = _video;
            _video = null;
            if (video != null)
            {
                video.TargetClosed -= OnTargetClosed;
                video.Faulted -= OnCaptureFaulted;
                try { video.Stop(); } catch { }      // emits the closing frame
                try { video.Dispose(); } catch { }
            }

            Recorder recorder = _recorder;
            if (recorder != null)
            {
                try { recorder.Stop(); } catch { }   // drains encoders + mux, finalizes the file
                LastRecordingCompleted = recorder.CompletedSuccessfully;
                bool empty = recorder.IsEmpty;
                recorder.Faulted -= OnWriteFaulted;
                try { recorder.Dispose(); } catch { }
                _recorder = null;

                // Stopped before anything was recorded: do not leave an unplayable stub behind.
                if (empty)
                    TryDeleteFile(_url);
            }

            GifRecorder gif = _gif;
            if (gif != null)
            {
                try { gif.Stop(); } catch { }
                LastRecordingCompleted = gif.CompletedSuccessfully;
                gif.Faulted -= OnWriteFaulted;
                try { gif.Dispose(); } catch { }
                _gif = null;
            }

            try { _ring?.Dispose(); } catch { }
            _ring = null;

            try { _device?.Dispose(); } catch { }
            _device = null;

            if (_latencyModeChanged)
            {
                try { GCSettings.LatencyMode = _previousLatencyMode; } catch { }
                _latencyModeChanged = false;
            }

            if (_powerHeld)
            {
                PowerHelper.RestoreSleep();
                PowerHelper.SetThrottlingDisabled(false);
                _powerHeld = false;
            }
        }

        #endregion

        #region Interruptions

        private void OnWriteFaulted(Exception error) => RaiseInterrupted(RecordingInterruption.WriteFailed, error);

        private void OnCaptureFaulted(Exception error) => RaiseInterrupted(RecordingInterruption.CaptureFailed, error);

        private void OnTargetClosed()
        {
            // Raised from a WinRT callback thread: never do the stop work there.
            ThreadPool.UnsafeQueueUserWorkItem(
                static state => ((ScreenEncoder)state).RaiseInterrupted(RecordingInterruption.TargetClosed, null), this);
        }

        private void RaiseInterrupted(RecordingInterruption reason, Exception error)
        {
            if (Interlocked.Exchange(ref _interruptRaised, 1) != 0)
                return;

            try { Interrupted?.Invoke(this, new RecordingInterruptedEventArgs(reason, error)); } catch { }
        }

        #endregion

        /// <summary>Stops a running recording (finalizing its file).</summary>
        public void Dispose() => Stop();
    }
}
