using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaEncoder;
using ScreenRecorder.DirectX;
using ScreenRecorder.Encoder;
using ScreenRecorder.Models;
using ScreenRecorder.Region;
using ScreenRecorder.Views;

namespace ScreenRecorder.Services
{
    /// <summary>Where the recorder is in its life cycle.</summary>
    public enum RecordingState
    {
        Idle,

        /// <summary>The pre-record countdown is running (Stop cancels it).</summary>
        Countdown,

        Recording,
        Paused,
    }

    /// <summary>
    /// The recording workflow, from "the user pressed record" to "the file is saved":
    /// resolves the capture target (asking the user to pick on screen when needed), runs the
    /// countdown, starts and stops the engine, shows the recording-area border, keeps the
    /// recorder's own windows out of the picture, and ends the recording gracefully when it
    /// cannot continue (window closed, write failure, disk full). The UI binds to its state;
    /// it has no UI of its own beyond what <see cref="IDialogService"/> shows.
    /// All members are for the UI thread.
    /// </summary>
    public sealed partial class RecordingService : ObservableObject, IDisposable, Controls.IAudioLevelSource
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan DiskCheckInterval = TimeSpan.FromSeconds(5);

        private readonly AppSettings _settings;
        private readonly IDialogService _dialogs;
        private readonly CaptureExclusionService _exclusion;
        private readonly ClickHighlightService _clickHighlight;
        private readonly ScreenEncoder _encoder = new ScreenEncoder();
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _timer;

        private CancellationTokenSource _countdownCancellation;
        private RecordingFrameWindow _frameWindow;
        private string _currentFile;
        private DateTime _lastDiskCheck;
        private bool _disposed;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsIdle))]
        [NotifyPropertyChangedFor(nameof(IsRecording))]
        [NotifyPropertyChangedFor(nameof(IsPaused))]
        [NotifyPropertyChangedFor(nameof(IsCountdown))]
        [NotifyPropertyChangedFor(nameof(IsBusy))]
        private RecordingState _state = RecordingState.Idle;

        /// <summary>What the current (or last) recording produces.</summary>
        [ObservableProperty]
        private RecordingKind _currentKind = RecordingKind.Video;

        /// <summary>Seconds left in the countdown (0 when none is running).</summary>
        [ObservableProperty]
        private int _countdownSeconds;

        /// <summary>Recorded time in whole seconds (paused time excluded).</summary>
        [ObservableProperty]
        private long _elapsedSeconds;

        /// <summary>Bytes written to the current file.</summary>
        [ObservableProperty]
        private long _bytesWritten;

        /// <summary>Frames lost because the encoder could not keep up.</summary>
        [ObservableProperty]
        private long _droppedFrames;

        /// <summary>True while a recording with sound is running: there is a level to show.</summary>
        [ObservableProperty]
        private bool _hasAudioLevel;

        /// <summary>Full path of the most recently finished recording, or null.</summary>
        [ObservableProperty]
        private string _lastSavedFile;

        /// <summary>
        /// True once the hardware encoders have been probed (see <see cref="HardwareEncoders"/>);
        /// until then nothing is known about them.
        /// </summary>
        [ObservableProperty]
        private bool _isHardwareDetected;

        public bool IsIdle => State == RecordingState.Idle;
        public bool IsRecording => State == RecordingState.Recording;
        public bool IsPaused => State == RecordingState.Paused;
        public bool IsCountdown => State == RecordingState.Countdown;

        /// <summary>True from the countdown until the recording has stopped.</summary>
        public bool IsBusy => State != RecordingState.Idle;

        /// <summary>Raised after a recording ended and its file was closed (argument: the file path).</summary>
        public event EventHandler<string> RecordingSaved;

        public RecordingService(AppSettings settings, IDialogService dialogs, CaptureExclusionService exclusion,
            ClickHighlightService clickHighlight = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _exclusion = exclusion ?? throw new ArgumentNullException(nameof(exclusion));
            _clickHighlight = clickHighlight;
            _dispatcher = Dispatcher.CurrentDispatcher;

            _encoder.CaptureStarting += OnCaptureStarting;
            _encoder.Stopped += OnEncoderStopped;
            _encoder.Interrupted += OnInterrupted;

            _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TickInterval };
            _timer.Tick += OnTick;
        }

        /// <summary>
        /// Probes the hardware encoders in the background (opening an encoder can take a
        /// moment) and publishes the result.
        /// </summary>
        public async Task DetectHardwareEncodersAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    HardwareEncoders.Detect();
                }
                catch
                {
                    // FFmpeg missing or broken: recording will report that when it starts.
                }
            });

            IsHardwareDetected = true;
        }

        #region Start

        /// <summary>
        /// Starts a recording of <paramref name="target"/> (the configured target when null),
        /// or resumes a paused one. Does nothing while a countdown is already running.
        /// <see cref="StartGifAsync"/> does the same for an animated GIF of an area the user
        /// picks on screen.
        /// </summary>
        public Task StartGifAsync() => StartAsync(CaptureTarget.UserSelection, RecordingKind.Gif);

        /// <inheritdoc cref="StartAsync(CaptureTarget)"/>
        public Task StartAsync(CaptureTarget target = null) => StartAsync(target, RecordingKind.Video);

        private async Task StartAsync(CaptureTarget target, RecordingKind kind)
        {
            switch (State)
            {
                case RecordingState.Paused:
                    Resume();
                    return;
                case RecordingState.Idle:
                    break;
                default:
                    return;
            }

            target ??= _settings.CaptureTarget;
            bool audioOnly = target.Kind == CaptureTargetKind.AudioOnly;

            if (!CheckRecordDirectory())
                return;

            ContainerFormat container = RecordingRequestBuilder.ResolveContainer(_settings, kind, audioOnly);
            if (container == null)
            {
                if (audioOnly)
                    _dialogs.ShowError(Properties.Resources.AudioOnlyNeedsSource);
                return;
            }

            if (audioOnly && !(_settings.RecordSystemAudio || _settings.RecordMicrophone))
            {
                _dialogs.ShowError(Properties.Resources.AudioOnlyNeedsSource);
                return;
            }

            ResolvedCaptureTarget resolved = ResolveTarget(target);
            if (resolved == null)
                return;

            if (!await RunCountdownAsync(_settings.RecordDelay))
                return;

            string filePath = RecordingRequestBuilder.BuildFilePath(_settings.RecordDirectory, DateTime.Now, container.Extension);
            if (File.Exists(filePath))
                return;

            if (!StartEncoder(resolved, container, filePath))
                return;

            _currentFile = filePath;
            CurrentKind = kind;
            ElapsedSeconds = 0;
            BytesWritten = 0;
            DroppedFrames = 0;
            _lastDiskCheck = DateTime.UtcNow;
            State = RecordingState.Recording;
            HasAudioLevel = _encoder.HasAudio;
            _timer.Start();

            ShowRecordingFrame(resolved);

            if (_settings.HighlightClicks && !resolved.AudioOnly)
                _clickHighlight?.Start();
        }

        private bool CheckRecordDirectory()
        {
            if (Directory.Exists(_settings.RecordDirectory))
                return true;

            _dialogs.ShowError(string.IsNullOrWhiteSpace(_settings.RecordDirectory)
                ? Properties.Resources.TheRecordingPathIsNotSet
                : Properties.Resources.RecordingPathDoesNotExist);
            return false;
        }

        /// <summary>
        /// Turns the chosen target into concrete capture coordinates, letting the user pick on
        /// screen for <see cref="CaptureTargetKind.UserSelection"/>. Null = cancelled / invalid.
        /// </summary>
        private ResolvedCaptureTarget ResolveTarget(CaptureTarget target)
        {
            switch (target.Kind)
            {
                case CaptureTargetKind.AudioOnly:
                    return ResolvedCaptureTarget.ForAudioOnly();

                case CaptureTargetKind.PrimaryDisplay:
                    return ResolvedCaptureTarget.ForDisplay(System.Windows.Forms.Screen.PrimaryScreen.DeviceName);

                case CaptureTargetKind.Display:
                    if (MonitorInfo.GetMonitorInfo(target.DeviceName) == null)
                    {
                        _dialogs.ShowError(Properties.Resources.DisplayNotFound);
                        return null;
                    }

                    return ResolvedCaptureTarget.ForDisplay(target.DeviceName);

                case CaptureTargetKind.AllDisplays:
                    Rect desktop = Rect.Empty;
                    foreach (MonitorInfo monitor in MonitorInfo.GetActiveMonitorInfos())
                        desktop.Union(monitor.Bounds);
                    return desktop.IsEmpty ? null : ResolvedCaptureTarget.ForDesktopRegion(desktop, showFrame: false);

                case CaptureTargetKind.UserSelection:
                    return ResolveUserSelection();

                default:
                    return null;
            }
        }

        private ResolvedCaptureTarget ResolveUserSelection()
        {
            RegionSelectionResult selection = _dialogs.SelectRegion(_settings.RegionSelectionMode, out RegionSelectionMode lastMode);
            _settings.RegionSelectionMode = lastMode;
            if (selection == null)
                return null;

            if (selection.Mode == RegionSelectionMode.DisplayRegion)
                return ResolvedCaptureTarget.ForDisplay(selection.DeviceName);

            // An area dragged across monitors has no single monitor: it is recorded as a
            // rectangle of the desktop.
            if (selection.DeviceName == null)
            {
                Rect? spanning = RecordingRequestBuilder.NormalizeRegion(selection.DesktopRegion);
                if (spanning == null)
                {
                    _dialogs.ShowError(Properties.Resources.RegionSizeError);
                    return null;
                }

                return ResolvedCaptureTarget.ForDesktopRegion(spanning.Value, showFrame: true);
            }

            Rect? region = RecordingRequestBuilder.NormalizeRegion(selection.Region);
            if (region == null)
            {
                _dialogs.ShowError(Properties.Resources.RegionSizeError);
                return null;
            }

            Rect desktopRegion = new Rect(selection.DesktopRegion.X, selection.DesktopRegion.Y, region.Value.Width, region.Value.Height);

            return selection.WindowHandle != IntPtr.Zero
                ? ResolvedCaptureTarget.ForWindow(selection.WindowHandle, selection.DeviceName, region.Value, desktopRegion)
                : ResolvedCaptureTarget.ForRegion(selection.DeviceName, region.Value, desktopRegion);
        }

        /// <summary>Counts down; false when it was cancelled (Stop) or the app is going away.</summary>
        private async Task<bool> RunCountdownAsync(int seconds)
        {
            if (seconds <= 0)
                return true;

            _countdownCancellation = new CancellationTokenSource();
            State = RecordingState.Countdown;
            try
            {
                for (int remaining = seconds; remaining > 0; remaining--)
                {
                    CountdownSeconds = remaining;
                    await Task.Delay(1000, _countdownCancellation.Token);
                }

                return !_disposed;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                CountdownSeconds = 0;
                _countdownCancellation.Dispose();
                _countdownCancellation = null;
                if (State == RecordingState.Countdown)
                    State = RecordingState.Idle;
            }
        }

        private bool StartEncoder(ResolvedCaptureTarget target, ContainerFormat container, string filePath)
        {
            try
            {
                _encoder.Start(RecordingRequestBuilder.Build(_settings, target, container, filePath));
                return true;
            }
            catch when (target.WindowHandle != IntPtr.Zero)
            {
                // Some windows cannot be captured as a window; record that part of the screen instead.
                TryDelete(filePath);
                return StartEncoder(target.AsRegion(), container, filePath);
            }
            catch
            {
                TryDelete(filePath);
                _dialogs.ShowError(Properties.Resources.FailedToStartRecording);
                return false;
            }
        }

        private static void TryDelete(string filePath)
        {
            try
            {
                if (File.Exists(filePath) && new FileInfo(filePath).Length == 0)
                    File.Delete(filePath);
            }
            catch
            {
            }
        }

        #endregion

        /// <summary>The loudest level recorded since the previous call (0..1) — for the level meter.</summary>
        public float ReadAudioPeak() => _encoder.ReadAudioPeak();

        #region Pause / resume / stop

        public void Pause()
        {
            if (State != RecordingState.Recording)
                return;

            _encoder.Pause();
            State = RecordingState.Paused;
        }

        public void Resume()
        {
            if (State != RecordingState.Paused)
                return;

            _encoder.Resume();
            State = RecordingState.Recording;
        }

        /// <summary>Stops the recording (the file is finalized before this returns), or cancels the countdown.</summary>
        public void Stop()
        {
            if (_countdownCancellation != null)
            {
                _countdownCancellation.Cancel();
                return;
            }

            if (State == RecordingState.Recording || State == RecordingState.Paused)
                _encoder.Stop(); // → OnEncoderStopped
        }

        #endregion

        #region Engine events

        private void OnCaptureStarting(object sender, EventArgs e)
        {
            // Raised inside Start, before the first frame is captured.
            _exclusion.SetActive(_settings.ExcludeFromCapture);
        }

        private void OnEncoderStopped(object sender, RecordingStoppedEventArgs e)
        {
            _timer.Stop();
            _clickHighlight?.Stop();
            CloseRecordingFrame();
            _exclusion.SetActive(false);

            string file = _currentFile;
            _currentFile = null;
            HasAudioLevel = false;
            State = RecordingState.Idle;

            if (file != null && File.Exists(file))
            {
                LastSavedFile = file;
                RecordingSaved?.Invoke(this, file);
            }
        }

        private void OnInterrupted(object sender, RecordingInterruptedEventArgs e)
        {
            // Raised on a pool thread: finish on the UI thread.
            _dispatcher.BeginInvoke(new Action(() => StopWithMessage(MessageFor(e.Reason))));
        }

        private static string MessageFor(RecordingInterruption reason)
        {
            switch (reason)
            {
                case RecordingInterruption.TargetClosed: return Properties.Resources.RecordingStoppedTargetClosed;
                case RecordingInterruption.WriteFailed: return Properties.Resources.RecordingStoppedWriteFailed;
                default: return Properties.Resources.RecordingStoppedCaptureFailed;
            }
        }

        private void StopWithMessage(string message)
        {
            if (State != RecordingState.Recording && State != RecordingState.Paused)
                return;

            Stop();
            _dialogs.ShowError(message);
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_encoder.IsRunning)
                return;

            RecordingStatistics stats = _encoder.GetStatistics();
            ElapsedSeconds = (long)stats.Elapsed.TotalSeconds; // notifies only when the second changes
            BytesWritten = stats.BytesWritten;
            DroppedFrames = stats.DroppedFrames;

            if (DateTime.UtcNow - _lastDiskCheck >= DiskCheckInterval)
            {
                _lastDiskCheck = DateTime.UtcNow;
                if (IsDiskAlmostFull(_currentFile))
                    StopWithMessage(Properties.Resources.RecordingStoppedLowDiskSpace);
            }
        }

        /// <summary>
        /// True when the drive holding <paramref name="filePath"/> is nearly out of space.
        /// Stopping early leaves a finalized file instead of a failed write.
        /// </summary>
        internal static bool IsDiskAlmostFull(string filePath)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(filePath));
                if (string.IsNullOrEmpty(root))
                    return false;

                return new DriveInfo(root).AvailableFreeSpace < RecordingRequestBuilder.MinimumFreeDiskBytes;
            }
            catch
            {
                return false; // network paths etc.: let the write itself decide
            }
        }

        #endregion

        #region Recording-area border

        private void ShowRecordingFrame(ResolvedCaptureTarget target)
        {
            if (!_settings.ShowRecordingFrame || target.AudioOnly || target.FrameRegion.IsEmpty)
                return;

            // The encoder falls back to region capture when a window cannot be captured as
            // itself; only follow the window when it really is being captured.
            IntPtr follow = target.WindowHandle;
            _frameWindow = new RecordingFrameWindow(target.FrameRegion, follow);
            _frameWindow.Show();
        }

        private void CloseRecordingFrame()
        {
            try
            {
                _frameWindow?.Close();
            }
            catch
            {
            }

            _frameWindow = null;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _countdownCancellation?.Cancel();
            _timer.Stop();

            _encoder.Interrupted -= OnInterrupted;
            _encoder.Dispose(); // stops and finalizes a running recording
            _encoder.CaptureStarting -= OnCaptureStarting;
            _encoder.Stopped -= OnEncoderStopped;

            CloseRecordingFrame();
            _clickHighlight?.Stop();
        }
    }
}
