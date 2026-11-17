using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomConverter;
using ScreenRecorder.Models;
using ScreenRecorder.Services;
using ScreenRecorder.Shortcut;

namespace ScreenRecorder.ViewModels
{
    /// <summary>
    /// The toolbar: recording status and time, and the commands behind its buttons, its
    /// context menu and the global keyboard shortcuts.
    /// </summary>
    public sealed partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IDialogService _dialogs;
        private readonly ShortcutService _shortcuts;

        public RecordingService Recording { get; }

        public AppSettings Settings { get; }

        public SettingsViewModel SettingsViewModel { get; }

        // Shortcuts exposed for the context menu's gesture texts.
        public ShortcutBinding StartShortcut { get; }
        public ShortcutBinding PauseShortcut { get; }
        public ShortcutBinding StopShortcut { get; }
        public ShortcutBinding RegionShortcut { get; }
        public ShortcutBinding GifShortcut { get; }

        /// <summary>"Waiting for recording", "Recording", "Paused"...</summary>
        [ObservableProperty]
        private string _statusText;

        /// <summary>Elapsed recording time (or the countdown) as hh:mm:ss.</summary>
        [ObservableProperty]
        private string _timeText = TimeFormatter.FormatSeconds(0);

        /// <summary>File size and dropped-frame count of the running recording.</summary>
        [ObservableProperty]
        private string _recordingInfo;

        public MainViewModel(RecordingService recording, SettingsStore store, SettingsViewModel settingsViewModel,
            ShortcutService shortcuts, IDialogService dialogs)
        {
            Recording = recording ?? throw new ArgumentNullException(nameof(recording));
            Settings = store?.Settings ?? throw new ArgumentNullException(nameof(store));
            SettingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
            _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

            Recording.PropertyChanged += OnRecordingChanged;
            Recording.RecordingSaved += OnRecordingSaved;

            StartShortcut = shortcuts.Register(ShortcutService.StartRecordingId, Properties.Resources.StartRecording, StartCommand);
            PauseShortcut = shortcuts.Register(ShortcutService.PauseRecordingId, Properties.Resources.PauseRecording, PauseCommand);
            StopShortcut = shortcuts.Register(ShortcutService.StopRecordingId, Properties.Resources.StopRecording, StopCommand);
            // Like Win+Shift+S for screenshots: from anywhere, pick an area and it records;
            // the same keys stop it again.
            RegionShortcut = shortcuts.Register(ShortcutService.RegionRecordingId, Properties.Resources.SelectRegionAndRecord,
                ToggleRegionRecordingCommand, new KeyGesture(Key.R, ModifierKeys.Alt | ModifierKeys.Shift));
            GifShortcut = shortcuts.Register(ShortcutService.GifRecordingId, Properties.Resources.SelectRegionAndRecordGif,
                ToggleGifRecordingCommand, new KeyGesture(Key.G, ModifierKeys.Alt | ModifierKeys.Shift));

            UpdateStatus();
        }

        #region Commands

        /// <summary>Starts recording the configured target, or resumes a paused recording.</summary>
        [RelayCommand(AllowConcurrentExecutions = true)]
        private Task StartAsync() => Recording.StartAsync();

        /// <summary>Lets the user pick an area / window / display, then records it.</summary>
        [RelayCommand(AllowConcurrentExecutions = true)]
        private Task RecordRegionAsync() => Recording.StartAsync(CaptureTarget.UserSelection);

        /// <summary>Records one window, chosen from the window list.</summary>
        [RelayCommand(AllowConcurrentExecutions = true)]
        private Task RecordWindowAsync(IntPtr windowHandle) => Recording.StartWindowAsync(windowHandle);

        /// <summary>Lets the user pick an area, then records it as an animated GIF.</summary>
        [RelayCommand(AllowConcurrentExecutions = true)]
        private Task RecordGifAsync() => Recording.StartGifAsync();

        /// <summary>Global-shortcut form of <see cref="RecordRegionAsync"/>: the same keys stop a running recording.</summary>
        [RelayCommand(AllowConcurrentExecutions = true)]
        private Task ToggleRegionRecordingAsync()
        {
            if (Recording.IsBusy)
            {
                Recording.Stop();
                return Task.CompletedTask;
            }

            return Recording.StartAsync(CaptureTarget.UserSelection);
        }

        /// <summary>Global-shortcut form of <see cref="RecordGifAsync"/>: the same keys stop a running recording.</summary>
        [RelayCommand(AllowConcurrentExecutions = true)]
        private Task ToggleGifRecordingAsync()
        {
            if (Recording.IsBusy)
            {
                Recording.Stop();
                return Task.CompletedTask;
            }

            return Recording.StartGifAsync();
        }

        [RelayCommand]
        private void Pause() => Recording.Pause();

        [RelayCommand]
        private void Stop() => Recording.Stop();

        [RelayCommand]
        private void OpenRecordFolder()
        {
            string directory = Settings.RecordDirectory;
            if (System.IO.Directory.Exists(directory))
            {
                string last = Recording.LastSavedFile;
                bool lastIsHere = last != null
                    && string.Equals(System.IO.Path.GetDirectoryName(last), directory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
                _dialogs.OpenFolder(directory, lastIsHere ? last : null);
                return;
            }

            _dialogs.ShowError(string.IsNullOrWhiteSpace(directory)
                ? Properties.Resources.TheRecordingPathIsNotSet
                : Properties.Resources.RecordingPathDoesNotExist);
        }

        [RelayCommand]
        private void OpenShortcutSettings() => _dialogs.ShowShortcutEditor(_shortcuts.Shortcuts);

        #endregion

        private void OnRecordingChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(RecordingService.State):
                case nameof(RecordingService.CountdownSeconds):
                case nameof(RecordingService.ElapsedSeconds):
                    UpdateStatus();
                    break;
                case nameof(RecordingService.BytesWritten):
                case nameof(RecordingService.DroppedFrames):
                    UpdateRecordingInfo();
                    break;
            }
        }

        private void UpdateStatus()
        {
            switch (Recording.State)
            {
                case RecordingState.Countdown:
                    StatusText = Properties.Resources.EncoderStatusCountdownMessage;
                    TimeText = TimeFormatter.FormatSeconds(Recording.CountdownSeconds);
                    break;
                case RecordingState.Recording:
                    StatusText = Properties.Resources.EncoderStatusStartMessage;
                    TimeText = TimeFormatter.FormatSeconds(Recording.ElapsedSeconds);
                    break;
                case RecordingState.Paused:
                    StatusText = Properties.Resources.EncoderStatusPauseMessage;
                    TimeText = TimeFormatter.FormatSeconds(Recording.ElapsedSeconds);
                    break;
                default:
                    StatusText = Properties.Resources.EncoderStatusStopMessage;
                    TimeText = TimeFormatter.FormatSeconds(0);
                    RecordingInfo = null;
                    break;
            }
        }

        private void UpdateRecordingInfo()
        {
            if (!Recording.IsBusy)
            {
                RecordingInfo = null;
                return;
            }

            string size = TimeFormatter.FormatBytes(Recording.BytesWritten);
            RecordingInfo = Recording.DroppedFrames > 0
                ? string.Format(CultureInfo.CurrentCulture, "{0} · {1}", size,
                    string.Format(CultureInfo.CurrentCulture, Properties.Resources.DroppedFramesFormat, Recording.DroppedFrames))
                : size;
        }

        private void OnRecordingSaved(object sender, string filePath) => _dialogs.ShowSavedNotification(filePath);

        public void Dispose()
        {
            Recording.PropertyChanged -= OnRecordingChanged;
            Recording.RecordingSaved -= OnRecordingSaved;
        }
    }
}
