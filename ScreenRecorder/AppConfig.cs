using System;
using System.Collections.Generic;
using System.Linq;
using MediaEncoder;
using ScreenRecorder.Config;

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

                config.Add(nameof(SelectedRecordFormat), SelectedRecordFormat);
                config.Add(nameof(SelectedRecordVideoCodec), Enum.GetName(typeof(VideoCodec), SelectedRecordVideoCodec));
                config.Add(nameof(SelectedRecordAudioCodec), Enum.GetName(typeof(AudioCodec), SelectedRecordAudioCodec));

                config.Add(nameof(SelectedRecordVideoBitrate), SelectedRecordVideoBitrate.ToString());
                config.Add(nameof(SelectedRecordAudioBitrate), SelectedRecordAudioBitrate.ToString());
                config.Add(nameof(RecordDirectory), RecordDirectory);

                config.Add(nameof(WindowWidth), WindowWidth.ToString());
                config.Add(nameof(WindowHeight), WindowHeight.ToString());
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
                    ScreenCaptureMonitor = Config.Config.GetString(config, nameof(ScreenCaptureMonitor), "");
                    ScreenCaptureCursorVisible = Config.Config.GetBool(config, nameof(ScreenCaptureCursorVisible), true);

                    AdvancedSettings = Config.Config.GetBool(config, nameof(AdvancedSettings), false);

                    SelectedRecordFormat = Config.Config.GetString(config, nameof(SelectedRecordFormat), "mp4");
                    SelectedRecordVideoCodec = Config.Config.GetEnum<VideoCodec>(config, nameof(SelectedRecordVideoCodec), VideoCodec.H264);
                    SelectedRecordAudioCodec = Config.Config.GetEnum<AudioCodec>(config, nameof(SelectedRecordAudioCodec), AudioCodec.Aac);
                    SelectedRecordVideoBitrate = Config.Config.GetInt32(config, nameof(SelectedRecordVideoBitrate), 5000000);
                    SelectedRecordAudioBitrate = Config.Config.GetInt32(config, nameof(SelectedRecordAudioBitrate), 160000);
                    RecordDirectory = Config.Config.GetString(config, nameof(RecordDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

                    WindowWidth = Config.Config.GetDouble(config, nameof(WindowWidth), -1.0d);
                    WindowHeight = Config.Config.GetDouble(config, nameof(WindowHeight), -1.0d);
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
            WindowWidth = -1.0d;
            WindowHeight = -1.0d;
            WindowLeft = -1.0d;
            WindowTop = -1.0d;

            ScreenCaptureMonitor = "";
            ScreenCaptureCursorVisible = true;

            AdvancedSettings = false;

            SelectedRecordFormat = "mp4";
            SelectedRecordVideoCodec = VideoCodec.H264;
            SelectedRecordAudioCodec = AudioCodec.Aac;
            SelectedRecordVideoBitrate = 5000000;
            SelectedRecordAudioBitrate = 160000;
            RecordDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
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
            }
        }

        #region Property

        #region Window
        private double windowWidth;
        public double WindowWidth
        {
            get => windowWidth;
            set => SetProperty(ref windowWidth, value);
        }

        private double windowHeight;
        public double WindowHeight
        {
            get => windowHeight;
            set => SetProperty(ref windowHeight, value);
        }

        private double windowLeft;
        public double WindowLeft
        {
            get => windowLeft;
            set => SetProperty(ref windowLeft, value);
        }

        private double windowTop;
        public double WindowTop
        {
            get => windowTop;
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

        private string recordDirectory;
        public string RecordDirectory
        {
            get => recordDirectory;
            set => SetProperty(ref recordDirectory, value);
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
