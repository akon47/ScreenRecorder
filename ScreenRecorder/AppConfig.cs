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
        #region 생성자
        private static volatile AppConfig instance;
        private static object syncRoot = new object();
        public static AppConfig Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (syncRoot)
                    {
                        if (instance == null)
                        {
                            instance = new AppConfig();
                        }
                    }
                }

                return instance;
            }
        }

        private readonly string ConfigFilePath = System.IO.Path.Combine(AppConstants.AppDataFolderPath, "config");

        private Object SyncObject = new object();
        private ConfigFileSaveWorker configFileSaveWorker;
        private volatile bool isDisposed = false;

        private AppConfig()
        {
            try
            {
                Load(ConfigFilePath);
            }
            catch { }

            Validation();

            configFileSaveWorker = new ConfigFileSaveWorker(this, ConfigFilePath);

            this.PropertyChanged += (s, e) =>
			{
				configFileSaveWorker?.SetModifiedConfigData();
			};
        }
        #endregion

        #region IConfigFile
        public void Save(string filePath)
        {
            lock (this)
            {
                Dictionary<string, string> config = new Dictionary<string, string>();
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
            lock (this)
            {
                Dictionary<string, string> config = Config.Config.LoadFromFile(filePath, true);

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

        public void Validation()
        {
            lock (this)
            {
                if(string.IsNullOrWhiteSpace(SelectedRecordFormat))
                {
                    SelectedRecordFormat = "mp4";
                }

                if(!AppManager.Instance.EncoderVideoCodecs?.Select(x => x.VideoCodec).Contains(SelectedRecordVideoCodec) ?? false)
                {
                    SelectedRecordVideoCodec = VideoCodec.H264;
                }

                if (!AppManager.Instance.EncoderAudioCodecs?.Select(x => x.AudioCodec).Contains(SelectedRecordAudioCodec) ?? false)
                {
                    SelectedRecordAudioCodec = AudioCodec.Aac;
                }

                if(string.IsNullOrWhiteSpace(ScreenCaptureMonitor))
                {
                    ScreenCaptureMonitor = CaptureTarget.PrimaryDisplay.DeviceName;
                }
            }
        }

        #region Properties

        #region Window
        private double windowLeft;
        public double WindowLeft
        {
            get => Math.Max(0, windowLeft);
            set => SetProperty(ref windowLeft, value);
        }

        private double windowTop;
        public double WindowTop
        {
            get => Math.Max(0, windowTop);
            set => SetProperty(ref windowTop, value);
        }
        #endregion

        #region Record
        private string selectedRecordFormat;
        public string SelectedRecordFormat
        {
            get => selectedRecordFormat;
            set => SetProperty(ref selectedRecordFormat, value);
        }

        private VideoCodec selectedRecordVideoCodec;
        public VideoCodec SelectedRecordVideoCodec
        {
            get => selectedRecordVideoCodec;
            set => SetProperty(ref selectedRecordVideoCodec, value);
        }

        private AudioCodec selectedRecordAudioCodec;
        public AudioCodec SelectedRecordAudioCodec
        {
            get => selectedRecordAudioCodec;
            set => SetProperty(ref selectedRecordAudioCodec, value);
        }

        private int selectedRecordVideoBitrate;
        public int SelectedRecordVideoBitrate
        {
            get => selectedRecordVideoBitrate;
            set => SetProperty(ref selectedRecordVideoBitrate, value);
        }

        private int selectedRecordAudioBitrate;
        public int SelectedRecordAudioBitrate
        {
            get => selectedRecordAudioBitrate;
            set => SetProperty(ref selectedRecordAudioBitrate, value);
        }

        private int selectedRecordFrameRate;
        public int SelectedRecordFrameRate
        {
            get => selectedRecordFrameRate;
            set => SetProperty(ref selectedRecordFrameRate, value);
        }

        private string recordDirectory;
        public string RecordDirectory
        {
            get => recordDirectory;
            set => SetProperty(ref recordDirectory, value);
        }

        private RegionSelectionMode regionSelectionMode;
        public RegionSelectionMode RegionSelectionMode
        {
            get => regionSelectionMode;
            set => SetProperty(ref regionSelectionMode, value);
        }

        private bool excludeFromCapture;
        public bool ExcludeFromCapture
        {
            get => excludeFromCapture;
            set => SetProperty(ref excludeFromCapture, value);
        }
        #endregion

        #region ScreenCapture
        private string screenCaptureMonitor;
        public string ScreenCaptureMonitor
        {
            get => screenCaptureMonitor;
            set => SetProperty(ref screenCaptureMonitor, value);
        }

        private bool screenCaptureCursorVisible;
        public bool ScreenCaptureCursorVisible
        {
            get => screenCaptureCursorVisible;
            set => SetProperty(ref screenCaptureCursorVisible, value);
        }
        #endregion

        #region Audio
        private bool recordMicrophone;
        public bool RecordMicrophone
        {
            get => recordMicrophone;
            set => SetProperty(ref recordMicrophone, value);
        }
        #endregion

        private bool advancedSettings;
        public bool AdvancedSettings
        {
            get => advancedSettings;
            set => SetProperty(ref advancedSettings, value);
        }

        #endregion

        public void Dispose()
        {
            try
            {
                lock (SyncObject)
                {
                    if (isDisposed)
                        return;

                    configFileSaveWorker?.Dispose();
                    configFileSaveWorker = null;

                    isDisposed = true;
                }
            }
            catch { }
        }
    }
}
