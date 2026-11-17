using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaEncoder;
using ScreenRecorder.Encoder;
using ScreenRecorder.Models;
using ScreenRecorder.Region;

namespace ScreenRecorder.Services
{
    /// <summary>A keyboard shortcut as stored in the settings file.</summary>
    public sealed class ShortcutSetting
    {
        /// <summary><see cref="Key.None"/> means "no shortcut" (the user removed it).</summary>
        public Key Key { get; set; } = Key.None;

        public ModifierKeys Modifiers { get; set; } = ModifierKeys.None;

        public static ShortcutSetting FromGesture(KeyGesture gesture)
        {
            return gesture == null
                ? new ShortcutSetting()
                : new ShortcutSetting { Key = gesture.Key, Modifiers = gesture.Modifiers };
        }

        /// <summary>The gesture, or null for "no shortcut" / a combination WPF does not accept.</summary>
        public KeyGesture ToGesture()
        {
            if (Key == Key.None)
                return null;

            try
            {
                return new KeyGesture(Key, Modifiers);
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The user's settings: an observable model the UI binds to, stored as <c>settings.json</c>
    /// by <see cref="SettingsStore"/> (the public read/write properties below ARE the file's
    /// schema — camelCase names, enums as text). Members marked <c>[JsonIgnore]</c> are derived
    /// values. Property names are part of the file format: rename one and old files stop
    /// loading that value.
    /// </summary>
    public sealed partial class AppSettings : ObservableObject
    {
        /// <summary>Version of the settings format this build writes.</summary>
        public const int CurrentVersion = 2;

        public static readonly IReadOnlyList<int> VideoBitrates = new[]
        {
            1000000, 2000000, 3000000, 4000000, 5000000, 6000000, 7000000, 8000000, 9000000,
            10000000, 15000000, 20000000, 30000000,
        };

        public static readonly IReadOnlyList<int> AudioBitrates = new[] { 64000, 128000, 160000, 192000, 320000 };

        /// <summary>Frame rates; <see cref="AutoFrameRate.Auto"/> (0) records only changed frames.</summary>
        public static readonly IReadOnlyList<int> FrameRates = new[] { AutoFrameRate.Auto, 15, 24, 25, 30, 48, 50, 60, 120, 144 };

        /// <summary>Countdown lengths (seconds) before recording starts.</summary>
        public static readonly IReadOnlyList<int> RecordDelays = new[] { 0, 3, 5, 10 };

        public static readonly IReadOnlyList<string> RecordFormats = new[] { "mp4", "avi", "matroska", "mpegts", "mov" };

        public const int DefaultGifFrameRate = 15;
        public const int DefaultGifScalePercent = 100;

        /// <summary>Highest frame rates offered for GIF recordings (only changed frames are stored).</summary>
        public static readonly IReadOnlyList<int> GifFrameRates = new[] { 10, 15, 20, 25, 30 };

        /// <summary>GIF output size as a percentage of the selected area.</summary>
        public static readonly IReadOnlyList<int> GifScalePercents = new[] { 100, 75, 50 };

        public static string DefaultRecordDirectory => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        /// <summary>Format version of the file these settings were read from.</summary>
        public int Version { get; set; } = CurrentVersion;

        #region Window

        [ObservableProperty]
        private double _windowLeft = -1;

        [ObservableProperty]
        private double _windowTop = -1;

        #endregion

        #region Recording format

        /// <summary>FFmpeg muxer short name of the container ("mp4", "matroska", ...).</summary>
        [ObservableProperty]
        private string _recordFormat = "mp4";

        [ObservableProperty]
        private VideoCodec _videoCodec = VideoCodec.H264;

        [ObservableProperty]
        private AudioCodec _audioCodec = AudioCodec.Aac;

        /// <summary>Video bitrate in bits per second (ignored in constant-quality mode).</summary>
        [ObservableProperty]
        private int _videoBitrate = 5000000;

        /// <summary>Audio bitrate in bits per second.</summary>
        [ObservableProperty]
        private int _audioBitrate = 160000;

        /// <summary>Frames per second, or <see cref="AutoFrameRate.Auto"/> (0).</summary>
        [ObservableProperty]
        private int _frameRate = AutoFrameRate.Auto;

        [ObservableProperty]
        private RecordQualityMode _qualityMode = RecordQualityMode.Bitrate;

        /// <summary>Seconds to count down before recording starts (0 = start immediately).</summary>
        [ObservableProperty]
        private int _recordDelay;

        [ObservableProperty]
        private string _recordDirectory = DefaultRecordDirectory;

        /// <summary>
        /// Advanced settings (codec, rate control, audio devices...) are shown — and applied —
        /// only while this is on; otherwise the proven defaults are used.
        /// </summary>
        [ObservableProperty]
        private bool _advancedSettings;

        #endregion

        #region GIF

        /// <summary>Highest frame rate of GIF recordings (advanced setting).</summary>
        [ObservableProperty]
        private int _gifFrameRate = DefaultGifFrameRate;

        /// <summary>GIF size as a percentage of the selected area (advanced setting).</summary>
        [ObservableProperty]
        private int _gifScalePercent = DefaultGifScalePercent;

        #endregion

        #region Capture

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CaptureTarget))]
        private CaptureTargetKind _captureTargetKind = CaptureTargetKind.PrimaryDisplay;

        /// <summary>Device name of the display to record when the kind is <see cref="CaptureTargetKind.Display"/>.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CaptureTarget))]
        private string _captureDisplayName = string.Empty;

        [ObservableProperty]
        private bool _captureCursor = true;

        /// <summary>Last mode used in the on-screen selector (area / window / display).</summary>
        [ObservableProperty]
        private RegionSelectionMode _regionSelectionMode = RegionSelectionMode.UserRegion;

        /// <summary>Keep the recorder's own windows out of the recording.</summary>
        [ObservableProperty]
        private bool _excludeFromCapture = true;

        /// <summary>Show a border around the recorded area / window while recording.</summary>
        [ObservableProperty]
        private bool _showRecordingFrame = true;

        /// <summary>
        /// The "still running in the notification area" hint has been shown: it explains where
        /// the toolbar went the first time it is minimized, and only then.
        /// </summary>
        [ObservableProperty]
        private bool _trayHintShown;

        /// <summary>Show a ring where the mouse is clicked, so clicks are visible in the recording.</summary>
        [ObservableProperty]
        private bool _highlightClicks;

        /// <summary>The capture target as one value (kind + display).</summary>
        [JsonIgnore]
        public CaptureTarget CaptureTarget
        {
            get => CaptureTarget.FromSettings(CaptureTargetKind, CaptureDisplayName);
            set
            {
                if (value == null)
                    return;

                CaptureDisplayName = value.DeviceName ?? string.Empty;
                CaptureTargetKind = value.Kind;
            }
        }

        #endregion

        #region Audio

        /// <summary>Record what the PC plays (WASAPI loopback).</summary>
        [ObservableProperty]
        private bool _recordSystemAudio = true;

        [ObservableProperty]
        private bool _recordMicrophone;

        /// <summary>Playback device to record; empty = follow the system default device.</summary>
        [ObservableProperty]
        private string _systemAudioDevice = string.Empty;

        /// <summary>Recording device for the microphone; empty = follow the system default device.</summary>
        [ObservableProperty]
        private string _microphoneDevice = string.Empty;

        #endregion

        // A list control with no matching item writes null back through its binding: keep the
        // "follow the system default" value (empty) instead.
        partial void OnSystemAudioDeviceChanged(string value)
        {
            if (value == null)
                SystemAudioDevice = string.Empty;
        }

        partial void OnMicrophoneDeviceChanged(string value)
        {
            if (value == null)
                MicrophoneDevice = string.Empty;
        }

        /// <summary>
        /// Global keyboard shortcuts by shortcut id (see <see cref="ShortcutService"/>). An id
        /// that is absent uses that shortcut's default.
        /// </summary>
        public Dictionary<string, ShortcutSetting> Shortcuts { get; set; } = new Dictionary<string, ShortcutSetting>();

        /// <summary>
        /// Replaces every value that is not among the offered choices with its default, so a
        /// hand-edited or outdated file can never put the app in an unusable state.
        /// </summary>
        public void Validate()
        {
            if (!Contains(RecordFormats, RecordFormat))
                RecordFormat = "mp4";

            if (VideoCodec != VideoCodec.H264 && VideoCodec != VideoCodec.H265 && VideoCodec != VideoCodec.Av1)
                VideoCodec = VideoCodec.H264;

            if (AudioCodec != AudioCodec.None && AudioCodec != AudioCodec.Aac && AudioCodec != AudioCodec.Mp3)
                AudioCodec = AudioCodec.Aac;

            if (!Contains(VideoBitrates, VideoBitrate))
                VideoBitrate = 5000000;

            if (!Contains(AudioBitrates, AudioBitrate))
                AudioBitrate = 160000;

            if (!Contains(FrameRates, FrameRate))
                FrameRate = AutoFrameRate.Auto;

            if (!Contains(RecordDelays, RecordDelay))
                RecordDelay = 0;

            if (!Contains(GifFrameRates, GifFrameRate))
                GifFrameRate = DefaultGifFrameRate;

            if (!Contains(GifScalePercents, GifScalePercent))
                GifScalePercent = DefaultGifScalePercent;

            if (!Enum.IsDefined(typeof(RecordQualityMode), QualityMode))
                QualityMode = RecordQualityMode.Bitrate;

            if (!Enum.IsDefined(typeof(RegionSelectionMode), RegionSelectionMode))
                RegionSelectionMode = RegionSelectionMode.UserRegion;

            if (!Enum.IsDefined(typeof(CaptureTargetKind), CaptureTargetKind)
                || (CaptureTargetKind == CaptureTargetKind.Display && string.IsNullOrWhiteSpace(CaptureDisplayName)))
            {
                CaptureTarget = CaptureTarget.PrimaryDisplay;
            }

            SystemAudioDevice ??= string.Empty;
            MicrophoneDevice ??= string.Empty;
            CaptureDisplayName ??= string.Empty;
            RecordDirectory ??= string.Empty;
            Shortcuts ??= new Dictionary<string, ShortcutSetting>();
            Version = CurrentVersion;
        }

        private static bool Contains<T>(IReadOnlyList<T> list, T value)
        {
            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < list.Count; i++)
            {
                if (comparer.Equals(list[i], value))
                    return true;
            }

            return false;
        }
    }
}
