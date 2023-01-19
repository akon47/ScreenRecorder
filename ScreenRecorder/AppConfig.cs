using System;
using System.Collections.Generic;
using System.Linq;
using MediaEncoder;
using ScreenRecorder.Config;
using ScreenRecorder.Region;

namespace ScreenRecorder
{
    public sealed class AppConfig : NotifyPropertyBase, IConfigFile, IDisposable
    {
        #region Constructors

        private static volatile AppConfig _instance;
        private static readonly object SyncRoot = new object();

        public static AppConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (SyncRoot)
                    {
                        if (_instance == null)
                        {
                            _instance = new AppConfig();
                        }
                    }
                }

                return _instance;
            }
        }

        private readonly string _configFilePath = System.IO.Path.Combine(AppConstants.AppDataFolderPath, "config");

        private readonly object _syncObject = new object();
        private ConfigFileSaveWorker _configFileSaveWorker;
        private volatile bool _isDisposed = false;

        private AppConfig()
        {
            try
            {
                Load(_configFilePath);
            }
            catch { }

            Validation();

            _configFileSaveWorker = new ConfigFileSaveWorker(this, _configFilePath);

            this.PropertyChanged += (s, e) =>
            {
                _configFileSaveWorker?.SetModifiedConfigData();
            };
        }

        #endregion


        #region IConfigFile

        public void Save(string filePath)
        {
            lock (_syncObject)
            {
                var config = new Dictionary<string, string>();
                config.Add(nameof(ScreenCaptureMonitor), ScreenCaptureMonitor);
                config.Add(nameof(ScreenCaptureCursorVisible), ScreenCaptureCursorVisible.ToString());

                config.Add(nameof(AdvancedSettings), AdvancedSettings.ToString());

                config.Add(nameof(ExcludeFromCapture), ExcludeFromCapture.ToString());

                config.Add(nameof(SelectedRecordFormat), SelectedRecordFormat);
                config.Add(nameof(SelectedRecordVideoCodec), Enum.GetName(typeof(VideoCodec), SelectedRecordVideoCodec));
                config.Add(nameof(SelectedRecordAudioCodec), Enum.GetName(typeof(AudioCodec), SelectedRecordAudioCodec));

                config.Add(nameof(SelectedRecordVideoBitrate), SelectedRecordVideoBitrate.ToString());
                config.Add(nameof(SelectedRecordAudioBitrate), SelectedRecordAudioBitrate.ToString());
                config.Add(nameof(SelectedRecordFrameRate), SelectedRecordFrameRate.ToString());

                config.Add(nameof(RecordDirectory), RecordDirectory);
                config.Add(nameof(RegionSelectionMode), Enum.GetName(typeof(RegionSelectionMode), RegionSelectionMode));

                config.Add(nameof(RecordMicrophone), RecordMicrophone.ToString());

                config.Add(nameof(WindowLeft), WindowLeft.ToString());
                config.Add(nameof(WindowTop), WindowTop.ToString());

                Config.Config.SaveToFile(filePath, config);
            }
        }

        public void Load(string filePath)
        {
            lock (_syncObject)
            {
                var config = Config.Config.LoadFromFile(filePath, true);

                if (config != null)
                {
                    ScreenCaptureMonitor = Config.Config.GetString(config, nameof(ScreenCaptureMonitor), CaptureTarget.PrimaryCaptureTargetDeviceName);
                    ScreenCaptureCursorVisible = Config.Config.GetBool(config, nameof(ScreenCaptureCursorVisible), true);

                    AdvancedSettings = Config.Config.GetBool(config, nameof(AdvancedSettings), false);

                    ExcludeFromCapture = Config.Config.GetBool(config, nameof(ExcludeFromCapture), true);

                    SelectedRecordFormat = Config.Config.GetString(config, nameof(SelectedRecordFormat), "mp4");
                    SelectedRecordVideoCodec = Config.Config.GetEnum<VideoCodec>(config, nameof(SelectedRecordVideoCodec), VideoCodec.H264);
                    SelectedRecordAudioCodec = Config.Config.GetEnum<AudioCodec>(config, nameof(SelectedRecordAudioCodec), AudioCodec.Aac);
                    SelectedRecordVideoBitrate = Config.Config.GetInt32(config, nameof(SelectedRecordVideoBitrate), 5000000);
                    SelectedRecordAudioBitrate = Config.Config.GetInt32(config, nameof(SelectedRecordAudioBitrate), 160000);
                    SelectedRecordFrameRate = Config.Config.GetInt32(config, nameof(SelectedRecordFrameRate), 60);
                    RecordDirectory = Config.Config.GetString(config, nameof(RecordDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
                    RegionSelectionMode = Config.Config.GetEnum<RegionSelectionMode>(config, nameof(RegionSelectionMode), RegionSelectionMode.UserRegion);

                    RecordMicrophone = Config.Config.GetBool(config, nameof(RecordMicrophone), false);

                    WindowLeft = Config.Config.GetDouble(config, nameof(WindowLeft), -1.0d);
                    WindowTop = Config.Config.GetDouble(config, nameof(WindowTop), -1.0d);
                }
                else
                {
                    SetDefault();
                }
            }
        }

        public void SetDefault()
        {
            WindowLeft = -1.0d;
            WindowTop = -1.0d;

            ScreenCaptureMonitor = CaptureTarget.PrimaryCaptureTargetDeviceName;
            ScreenCaptureCursorVisible = true;

            AdvancedSettings = false;

            ExcludeFromCapture = true;

            SelectedRecordFormat = "mp4";
            SelectedRecordVideoCodec = VideoCodec.H264;
            SelectedRecordAudioCodec = AudioCodec.Aac;
            SelectedRecordVideoBitrate = 5000000;
            SelectedRecordAudioBitrate = 160000;
            SelectedRecordFrameRate = 60;
            RecordDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            RegionSelectionMode = RegionSelectionMode.UserRegion;

            RecordMicrophone = false;
        }

        #endregion


        #region Properties

        #region Window
        private double _windowLeft;
        public double WindowLeft
        {
            get => Math.Max(0, _windowLeft);
            set => SetProperty(ref _windowLeft, value);
        }

        private double _windowTop;
        public double WindowTop
        {
            get => Math.Max(0, _windowTop);
            set => SetProperty(ref _windowTop, value);
        }
        #endregion


        #region Record

        private string _selectedRecordFormat;

        public string SelectedRecordFormat
        {
            get => _selectedRecordFormat;
            set => SetProperty(ref _selectedRecordFormat, value);
        }


        private VideoCodec _selectedRecordVideoCodec;

        public VideoCodec SelectedRecordVideoCodec
        {
            get => _selectedRecordVideoCodec;
            set => SetProperty(ref _selectedRecordVideoCodec, value);
        }


        private AudioCodec _selectedRecordAudioCodec;

        public AudioCodec SelectedRecordAudioCodec
        {
            get => _selectedRecordAudioCodec;
            set => SetProperty(ref _selectedRecordAudioCodec, value);
        }


        private IEnumerable<int> _videoBitrates;

        public IEnumerable<int> VideoBitrates
        {
            get
            {
                if (_videoBitrates == null)
                {
                    _videoBitrates = new int[]
                    {
                        1000000,
                        2000000,
                        3000000,
                        4000000,
                        5000000,
                        6000000,
                        7000000,
                        8000000,
                        9000000,
                        10000000,
                        15000000,
                        20000000,
                        30000000,
                    };
                }

                return _videoBitrates;
            }
        }


        private int _selectedRecordVideoBitrate;

        public int SelectedRecordVideoBitrate
        {
            get => _selectedRecordVideoBitrate;
            set => SetProperty(ref _selectedRecordVideoBitrate, value);
        }


        private IEnumerable<int> _audioBitrates;

        public IEnumerable<int> AudioBitrates
        {
            get
            {
                if (_audioBitrates == null)
                {
                    _audioBitrates = new int[]
                    {
                        64000,
                        128000,
                        160000,
                        192000,
                        320000,
                    };
                }

                return _audioBitrates;
            }
        }


        private int _selectedRecordAudioBitrate;

        public int SelectedRecordAudioBitrate
        {
            get => _selectedRecordAudioBitrate;
            set => SetProperty(ref _selectedRecordAudioBitrate, value);
        }


        private IEnumerable<int> _recordFramerates;

        public IEnumerable<int> RecordFramerates
        {
            get
            {
                if (_recordFramerates == null)
                {
                    _recordFramerates = new int[]
                    {
                        15,
                        24,
                        25,
                        30,
                        48,
                        50,
                        60,
                        120,
                        144,
                    };
                }

                return _recordFramerates;
            }
        }


        private int _selectedRecordFrameRate;

        public int SelectedRecordFrameRate
        {
            get => _selectedRecordFrameRate;
            set => SetProperty(ref _selectedRecordFrameRate, value);
        }


        private string _recordDirectory;

        public string RecordDirectory
        {
            get => _recordDirectory;
            set => SetProperty(ref _recordDirectory, value);
        }


        private RegionSelectionMode _regionSelectionMode;

        public RegionSelectionMode RegionSelectionMode
        {
            get => _regionSelectionMode;
            set => SetProperty(ref _regionSelectionMode, value);
        }


        private bool _excludeFromCapture;

        public bool ExcludeFromCapture
        {
            get => _excludeFromCapture;
            set => SetProperty(ref _excludeFromCapture, value);
        }

        #endregion


        #region ScreenCapture

        private string _screenCaptureMonitor;

        public string ScreenCaptureMonitor
        {
            get => _screenCaptureMonitor;
            set => SetProperty(ref _screenCaptureMonitor, value);
        }


        private bool _screenCaptureCursorVisible;

        public bool ScreenCaptureCursorVisible
        {
            get => _screenCaptureCursorVisible;
            set => SetProperty(ref _screenCaptureCursorVisible, value);
        }

        #endregion


        #region Audio

        private bool _recordMicrophone;

        public bool RecordMicrophone
        {
            get => _recordMicrophone;
            set => SetProperty(ref _recordMicrophone, value);
        }

        #endregion

        private bool _advancedSettings;

        public bool AdvancedSettings
        {
            get => _advancedSettings;
            set => SetProperty(ref _advancedSettings, value);
        }

        #endregion


        #region Helpers

        private void Validation()
        {
            lock (this)
            {
                if (string.IsNullOrWhiteSpace(SelectedRecordFormat))
                {
                    SelectedRecordFormat = "mp4";
                }

                if (!AppManager.Instance.EncoderVideoCodecs?.Select(x => x.VideoCodec).Contains(SelectedRecordVideoCodec) ?? false)
                {
                    SelectedRecordVideoCodec = VideoCodec.H264;
                }

                if (!AppManager.Instance.EncoderAudioCodecs?.Select(x => x.AudioCodec).Contains(SelectedRecordAudioCodec) ?? false)
                {
                    SelectedRecordAudioCodec = AudioCodec.Aac;
                }

                if (string.IsNullOrWhiteSpace(ScreenCaptureMonitor))
                {
                    ScreenCaptureMonitor = CaptureTarget.PrimaryDisplay.DeviceName;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                lock (_syncObject)
                {
                    if (_isDisposed)
                        return;

                    _configFileSaveWorker?.Dispose();
                    _configFileSaveWorker = null;

                    _isDisposed = true;
                }
            }
            catch { }
        }

        #endregion
    }
}
