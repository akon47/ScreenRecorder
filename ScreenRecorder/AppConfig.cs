using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Globalization;
using MediaEncoder;
using ScreenRecorder.Config;
using ScreenRecorder.Region;
using ScreenRecorder.DirectX;

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
                config.Add(nameof(ScreenCaptureRect), ScreenCaptureRect?.ToString(CultureInfo.InvariantCulture)??"");

                config.Add(nameof(AdvancedSettings), AdvancedSettings.ToString());

                config.Add(nameof(SelectedRecordFormat), SelectedRecordFormat);
                config.Add(nameof(SelectedRecordVideoCodec), Enum.GetName(typeof(VideoCodec), SelectedRecordVideoCodec));
                config.Add(nameof(SelectedRecordAudioCodec), Enum.GetName(typeof(AudioCodec), SelectedRecordAudioCodec));

                config.Add(nameof(SelectedRecordVideoBitrate), SelectedRecordVideoBitrate.ToString());
                config.Add(nameof(SelectedRecordAudioBitrate), SelectedRecordAudioBitrate.ToString());
                config.Add(nameof(SelectedRecordFrameRate), SelectedRecordFrameRate.ToString());

                config.Add(nameof(RecordDirectory), RecordDirectory);
                config.Add(nameof(RegionSelectionMode), Enum.GetName(typeof(RegionSelectionMode), RegionSelectionMode));

                config.Add(nameof(RecordMicrophone), RecordMicrophone.ToString());

                config.Add(nameof(WindowLeft), WindowLeft.ToString(CultureInfo.InvariantCulture));
                config.Add(nameof(WindowTop), WindowTop.ToString(CultureInfo.InvariantCulture));

                config.Add(nameof(CaptureTimeControlled), CaptureTimeControlled.ToString());
                config.Add(nameof(CaptureStartTime), CaptureStartTime.ToString(CultureInfo.InvariantCulture));
                config.Add(nameof(CaptureEndTime), CaptureEndTime.ToString(CultureInfo.InvariantCulture));
                config.Add(nameof(ExitProgram), ExitProgram.ToString());
                config.Add(nameof(ShutDown), ShutDown.ToString());

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
                    ScreenCaptureRect = Config.Config.GetRect(config, nameof(ScreenCaptureRect), null);
                    AppManager.Instance.ScreenCaptureMonitorDescription = MonitorInfo.GetMonitorInfo(ScreenCaptureMonitor)?.Description ?? "";

                    AdvancedSettings = Config.Config.GetBool(config, nameof(AdvancedSettings), false);

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

                    CaptureTimeControlled = Config.Config.GetBool(config, nameof(CaptureTimeControlled), false);
                    var now = DateTime.Now;
                    CaptureStartTime = Config.Config.GetDateTime(config, nameof(CaptureStartTime), now);
                    if (CaptureStartTime < now)
                        CaptureStartTime = now;
                    CaptureStartTime -= TimeSpan.FromMilliseconds(CaptureStartTime.Second * 1000 + CaptureStartTime.Millisecond);
                    CaptureEndTime = Config.Config.GetDateTime(config, nameof(CaptureEndTime), now + TimeSpan.FromMinutes(5));
                    if (CaptureEndTime < CaptureStartTime)
                        CaptureEndTime = CaptureStartTime + TimeSpan.FromMinutes(5);
                    CaptureEndTime -= TimeSpan.FromMilliseconds(CaptureEndTime.Second * 1000 + CaptureEndTime.Millisecond);
                    ExitProgram = Config.Config.GetBool(config, nameof(ExitProgram), false);
                    ShutDown = Config.Config.GetBool(config, nameof(ShutDown), false);
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

        #region Property

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

        public Rect? ScreenCaptureRect
        {
            get => new Rect(ScreenCaptureRectLeft, ScreenCaptureRectTop, ScreenCaptureRectWidth, screenCaptureRectHeight);
            set
            {
                if (value.HasValue)
                {
                    ScreenCaptureRectTop = value.Value.Top;
                    ScreenCaptureRectLeft = value.Value.Left;
                    ScreenCaptureRectWidth = value.Value.Width;
                    ScreenCaptureRectHeight = value.Value.Height;
                }
                else
                {
                    ScreenCaptureRectTop = 0;
                    ScreenCaptureRectLeft = 0;
                    ScreenCaptureRectWidth = 0;
                    ScreenCaptureRectHeight = 0;
                }
            }
        }

        private double screenCaptureRectLeft;
        public double ScreenCaptureRectLeft
        {
            get => Math.Max(0, screenCaptureRectLeft);
            set => SetProperty(ref screenCaptureRectLeft, value);
        }

        private double screenCaptureRectTop;
        public double ScreenCaptureRectTop
        {
            get => Math.Max(0, screenCaptureRectTop);
            set => SetProperty(ref screenCaptureRectTop, value);
        }

        private double screenCaptureRectWidth;
        public double ScreenCaptureRectWidth
        {
            get => Math.Max(0, screenCaptureRectWidth);
            set => SetProperty(ref screenCaptureRectWidth, value);
        }

        private double screenCaptureRectHeight;
        public double ScreenCaptureRectHeight
        {
            get => Math.Max(0, screenCaptureRectHeight);
            set => SetProperty(ref screenCaptureRectHeight, value);
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

        #region Time controlled capture

        private bool captureTimeControlled;
        public bool CaptureTimeControlled
        {
            get => captureTimeControlled;
            set => SetProperty(ref captureTimeControlled, value);
        }

        private DateTime captureStartTime;
        public DateTime CaptureStartTime
        {
            get => captureStartTime;
            set => SetProperty(ref captureStartTime, value);
        }

        private DateTime captureEndTime;
        public DateTime CaptureEndTime
        {
            get => captureEndTime;
            set => SetProperty(ref captureEndTime, value);
        }

        private bool exitProgram;
        public bool ExitProgram
        {
            get => exitProgram;
            set
            {
                if (SetProperty(ref exitProgram, value) && !value)
                    ShutDown = false;
            }
        }

        private bool shutDown;
        public bool ShutDown
        {
            get => shutDown;
            set
            {
                if (SetProperty(ref shutDown, value) && value)
                    ExitProgram = true;
            }
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
