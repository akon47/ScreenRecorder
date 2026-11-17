using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaEncoder;
using ScreenRecorder.AudioSource;
using ScreenRecorder.DirectX;
using ScreenRecorder.Models;
using ScreenRecorder.Services;

namespace ScreenRecorder.ViewModels
{
    /// <summary>
    /// The settings flyout: the choices offered for each setting (formats, codecs, displays,
    /// audio devices...) and the few derived states the view needs (warnings, what is
    /// currently applicable). The values themselves live in <see cref="AppSettings"/>.
    /// </summary>
    public sealed partial class SettingsViewModel : ObservableObject, IDisposable
    {
        private readonly IDialogService _dialogs;

        public AppSettings Settings { get; }

        public RecordingService Recording { get; }

        public IReadOnlyList<ContainerFormat> Formats { get; }
        /// <summary>
        /// The video codecs that can be used right now, each labeled with the encoder that will
        /// do the work on this PC ("H.264 · NVIDIA NVENC", "H.265 · Software (CPU)"...). AV1
        /// is listed only where a hardware encoder for it exists and the selected file format
        /// can hold it.
        /// </summary>
        [ObservableProperty]
        private IReadOnlyList<OptionItem<VideoCodec>> _videoCodecs = Array.Empty<OptionItem<VideoCodec>>();
        public IReadOnlyList<OptionItem<AudioCodec>> AudioCodecs { get; }
        public IReadOnlyList<OptionItem<RecordQualityMode>> QualityModes { get; }
        public IReadOnlyList<int> VideoBitrates => AppSettings.VideoBitrates;
        public IReadOnlyList<int> AudioBitrates => AppSettings.AudioBitrates;
        public IReadOnlyList<int> FrameRates => AppSettings.FrameRates;
        public IReadOnlyList<int> RecordDelays => AppSettings.RecordDelays;
        public IReadOnlyList<int> GifFrameRates => AppSettings.GifFrameRates;
        public IReadOnlyList<int> GifScalePercents => AppSettings.GifScalePercents;

        /// <summary>User selection, primary display, each connected display, audio only.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedCaptureTarget))]
        [NotifyPropertyChangedFor(nameof(IsCaptureTargetMissing))]
        private IReadOnlyList<CaptureTarget> _captureTargets = Array.Empty<CaptureTarget>();

        /// <summary>Playback devices, preceded by "System default".</summary>
        [ObservableProperty]
        private IReadOnlyList<AudioDeviceInfo> _audioOutputDevices = Array.Empty<AudioDeviceInfo>();

        /// <summary>Recording devices, preceded by "System default".</summary>
        [ObservableProperty]
        private IReadOnlyList<AudioDeviceInfo> _audioInputDevices = Array.Empty<AudioDeviceInfo>();

        public SettingsViewModel(SettingsStore store, RecordingService recording, IDialogService dialogs)
        {
            Settings = store?.Settings ?? throw new ArgumentNullException(nameof(store));
            Recording = recording ?? throw new ArgumentNullException(nameof(recording));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

            Formats = AppSettings.RecordFormats.Select(ContainerFormat.FromFormat).Where(f => f != null).ToArray();
            AudioCodecs = new[]
            {
                new OptionItem<AudioCodec>(AudioCodec.None, "No Audio"),
                new OptionItem<AudioCodec>(AudioCodec.Aac, "AAC (Advanced Audio Coding)"),
                new OptionItem<AudioCodec>(AudioCodec.Mp3, "MP3 (MPEG audio layer 3)"),
            };
            QualityModes = new[]
            {
                new OptionItem<RecordQualityMode>(RecordQualityMode.Bitrate, Properties.Resources.QualityModeBitrate),
                new OptionItem<RecordQualityMode>(RecordQualityMode.High, Properties.Resources.QualityModeHigh),
                new OptionItem<RecordQualityMode>(RecordQualityMode.Medium, Properties.Resources.QualityModeMedium),
                new OptionItem<RecordQualityMode>(RecordQualityMode.Low, Properties.Resources.QualityModeLow),
            };

            Settings.PropertyChanged += OnSettingsChanged;
            Recording.PropertyChanged += OnRecordingChanged;

            RefreshCaptureTargets();
            RefreshVideoCodecs();
        }

        private void RefreshVideoCodecs()
        {
            bool detected = Recording.IsHardwareDetected;
            var codecs = new List<OptionItem<VideoCodec>>();

            foreach (var (codec, name) in new[] { (VideoCodec.H264, "H.264"), (VideoCodec.H265, "H.265 (HEVC)"), (VideoCodec.Av1, "AV1") })
            {
                if (!MediaFormat.SupportsVideoCodec(Settings.RecordFormat, codec))
                    continue;

                if (!detected)
                {
                    // Not probed yet: the two codecs that always work (software at worst).
                    if (codec != VideoCodec.Av1)
                        codecs.Add(new OptionItem<VideoCodec>(codec, name));
                    continue;
                }

                HwAccel backend = HardwareEncoders.GetBest(codec);
                if (codec == VideoCodec.Av1 && backend == HwAccel.Software)
                    continue;

                codecs.Add(new OptionItem<VideoCodec>(codec, $"{name} · {BackendName(backend)}"));
            }

            VideoCodecs = codecs;

            // The stored choice may not be offered any more (other file format, other PC).
            if (codecs.Count > 0 && !codecs.Any(c => c.Value == Settings.VideoCodec))
                Settings.VideoCodec = VideoCodec.H264;
            else
                OnPropertyChanged(nameof(SelectedVideoCodec));

            OnPropertyChanged(nameof(ShowSoftwareEncoderWarning));
        }

        /// <summary>
        /// The selected codec. Goes through the view model (not straight to the settings) so
        /// the selection survives the list being rebuilt.
        /// </summary>
        public VideoCodec SelectedVideoCodec
        {
            get => Settings.VideoCodec;
            set
            {
                if (VideoCodecs.Any(c => c.Value == value))
                    Settings.VideoCodec = value;
            }
        }

        private static string BackendName(HwAccel backend)
        {
            switch (backend)
            {
                case HwAccel.Nvenc: return "NVIDIA NVENC";
                case HwAccel.Qsv: return "Intel Quick Sync";
                case HwAccel.Amf: return "AMD AMF";
                default: return Properties.Resources.SoftwareEncoder;
            }
        }

        /// <summary>Re-reads the connected displays and audio devices (called when the flyout opens).</summary>
        public void Refresh()
        {
            RefreshCaptureTargets();
            RefreshAudioDevices();
        }

        private void RefreshCaptureTargets()
        {
            MonitorInfo[] monitors = MonitorInfo.GetActiveMonitorInfos();
            var targets = new List<CaptureTarget> { CaptureTarget.UserSelection, CaptureTarget.PrimaryDisplay };
            targets.AddRange(monitors.Select(CaptureTarget.ForDisplay));
            if (monitors.Length > 1)
                targets.Add(CaptureTarget.AllDisplays);
            targets.Add(CaptureTarget.AudioOnly);
            CaptureTargets = targets;
        }

        private void RefreshAudioDevices()
        {
            var systemDefault = new AudioDeviceInfo(string.Empty, Properties.Resources.DefaultDevice);
            AudioOutputDevices = new[] { systemDefault }.Concat(AudioDevices.GetOutputDevices()).ToArray();
            AudioInputDevices = new[] { systemDefault }.Concat(AudioDevices.GetInputDevices()).ToArray();
        }

        /// <summary>
        /// The configured target as an entry of <see cref="CaptureTargets"/>; null when it is a
        /// display that is no longer connected (the view then shows "display not found").
        /// </summary>
        public CaptureTarget SelectedCaptureTarget
        {
            get => CaptureTargets.FirstOrDefault(t => t.Equals(Settings.CaptureTarget));
            set
            {
                if (value != null)
                    Settings.CaptureTarget = value;
            }
        }

        public bool IsCaptureTargetMissing => SelectedCaptureTarget == null;

        /// <summary>Recording sound only: the video settings do not apply.</summary>
        public bool IsAudioOnly => Settings.CaptureTargetKind == CaptureTargetKind.AudioOnly;

        /// <summary>The bitrate applies unless a constant-quality mode is active.</summary>
        public bool IsVideoBitrateApplicable
            => RecordingRequestBuilder.EffectiveQualityMode(Settings) == RecordQualityMode.Bitrate;

        /// <summary>The codec that will be used has no working hardware encoder on this PC.</summary>
        public bool ShowSoftwareEncoderWarning
            => Recording.IsHardwareDetected
               && !IsAudioOnly
               && !HardwareEncoders.IsAvailable(RecordingRequestBuilder.EffectiveVideoCodec(Settings));

        [RelayCommand]
        private void BrowseRecordDirectory()
        {
            string folder = _dialogs.PickFolder(Properties.Resources.SetsTheRecordingPath, Settings.RecordDirectory);
            if (folder != null)
                Settings.RecordDirectory = folder;
        }

        [RelayCommand]
        private void OpenRecordDirectory() => _dialogs.OpenFolder(Settings.RecordDirectory);

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AppSettings.CaptureTarget):
                    OnPropertyChanged(nameof(SelectedCaptureTarget));
                    OnPropertyChanged(nameof(IsCaptureTargetMissing));
                    OnPropertyChanged(nameof(IsAudioOnly));
                    OnPropertyChanged(nameof(ShowSoftwareEncoderWarning));
                    break;
                case nameof(AppSettings.RecordFormat):
                    RefreshVideoCodecs();
                    break;
                case nameof(AppSettings.VideoCodec):
                    OnPropertyChanged(nameof(SelectedVideoCodec));
                    OnPropertyChanged(nameof(ShowSoftwareEncoderWarning));
                    break;
                case nameof(AppSettings.AdvancedSettings):
                case nameof(AppSettings.QualityMode):
                    OnPropertyChanged(nameof(IsVideoBitrateApplicable));
                    OnPropertyChanged(nameof(ShowSoftwareEncoderWarning));
                    break;
            }
        }

        private void OnRecordingChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RecordingService.IsHardwareDetected))
                RefreshVideoCodecs();
        }

        public void Dispose()
        {
            Settings.PropertyChanged -= OnSettingsChanged;
            Recording.PropertyChanged -= OnRecordingChanged;
        }
    }
}
