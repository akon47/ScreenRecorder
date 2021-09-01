using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ScreenRecorder.Command;
using ScreenRecorder.Config;
using ScreenRecorder.Encoder;

namespace ScreenRecorder
{
    public sealed class AppCommands : IConfig, IConfigFile, IDisposable
    {
        #region Constructor
        private static volatile AppCommands instance;
        private static object syncRoot = new object();
        public static AppCommands Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (syncRoot)
                    {
                        if (instance == null)
                        {
                            instance = new AppCommands();
                        }
                    }
                }

                return instance;
            }
        }

        private readonly string ConfigFilePath = System.IO.Path.Combine(AppConstants.AppDataFolderPath, "commands");
        private ConfigFileSaveWorker configFileSaveWorker;
        private volatile bool isDisposed = false;

        private AppCommands()
        {
            Load(ConfigFilePath);

            foreach (var propertyInfo in this.GetType().GetProperties())
            {
                if (propertyInfo.PropertyType == typeof(DelegateCommand))
                {
                    var command = propertyInfo.GetValue(this, null) as DelegateCommand;
                    if (command != null)
                    {
                        command.WhenChanged(() =>
                        {
                            configFileSaveWorker?.SetModifiedConfigData();
                        },
                        nameof(DelegateCommand.KeyGesture));
                    }
                }
            }

            EventManager.RegisterClassHandler(typeof(Window), System.Windows.Input.Keyboard.PreviewKeyDownEvent, new KeyEventHandler(PreviewKeyDown), true);

            configFileSaveWorker = new ConfigFileSaveWorker(this, ConfigFilePath);
        }

        private void PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            foreach (var propertyInfo in this.GetType().GetProperties())
            {
                if (propertyInfo.PropertyType == typeof(DelegateCommand))
                {
                    var command = propertyInfo.GetValue(this, null) as DelegateCommand;
                    if (command != null && command.KeyGesture != null)
                    {
                        if (Keyboard.Modifiers.HasFlag(command.Modifiers) && key == command.Key)
                        {
                            command.Execute();
                        }
                    }
                }
            }
        }
        #endregion

        #region IConfig

        public Dictionary<string, string> SaveConfig()
        {
            Dictionary<string, string> config = new Dictionary<string, string>();

            foreach (var propertyInfo in this.GetType().GetProperties())
            {
                if (propertyInfo.PropertyType == typeof(DelegateCommand))
                {
                    var command = propertyInfo.GetValue(this, null) as DelegateCommand;
                    if (command != null)
                    {
                        config.Add(propertyInfo.Name, Config.Config.SaveToString(command));
                    }
                }
            }

            return config;
        }

        public void LoadConfig(Dictionary<string, string> config)
        {
            if (config != null)
            {
                foreach (var propertyInfo in this.GetType().GetProperties())
                {
                    if (propertyInfo.PropertyType == typeof(DelegateCommand))
                    {
                        var command = propertyInfo.GetValue(this, null) as DelegateCommand;
                        if (command != null)
                        {
                            if (config.ContainsKey(propertyInfo.Name))
                            {
                                command.LoadConfig(Config.Config.GetConfig(config, propertyInfo.Name));
                            }
                            else
                            {
                                command.KeyGesture = command.DefaultKeyGesture;
                            }
                        }
                    }
                }
            }
            else
            {
                SetDefault();
            }
        }

        public void SetDefault()
        {
            foreach (var propertyInfo in this.GetType().GetProperties())
            {
                if (propertyInfo.PropertyType == typeof(DelegateCommand))
                {
                    var command = propertyInfo.GetValue(this, null) as DelegateCommand;
                    if (command != null && command.KeyGesture != command.DefaultKeyGesture)
                    {
                        command.KeyGesture = command.DefaultKeyGesture;
                    }
                }
            }
        }

        #endregion

        #region IConfigFile
        public void Save(string filePath)
        {
            Config.Config.SaveToFile(filePath, this.SaveConfig());
        }

        public void Load(string filePath)
        {
            this.LoadConfig(Config.Config.LoadFromFile(filePath, true));
        }
        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (isDisposed)
                return;

            configFileSaveWorker?.Dispose();
            configFileSaveWorker = null;

            isDisposed = true;
        }
        #endregion

        private DelegateCommand startScreenRecordCommand;
        private DelegateCommand pauseScreenRecordCommand;
        private DelegateCommand stopScreenRecordCommand;

        private DelegateCommand openFolderInWindowExplorerCommand;

        private DelegateCommand openRecordDirecotryCommand;

        private DelegateCommand selectRecordDirectory;

        #region Record Commands
        public DelegateCommand StartScreenRecordCommand => startScreenRecordCommand ??
            (startScreenRecordCommand = new DelegateCommand(o =>
            {
                if (AppManager.Instance.ScreenEncoder.Status == Encoder.EncoderStatus.Stop)
                {
                    EncoderFormat encodeFormat = AppManager.Instance.EncoderFormats.First((x => x.Format.Equals(AppConfig.Instance.SelectedRecordFormat, StringComparison.OrdinalIgnoreCase)));
                    if (encodeFormat != null)
                    {
                        if (!System.IO.Directory.Exists(AppConfig.Instance.RecordDirectory))
                        {
                            if (string.IsNullOrWhiteSpace(AppConfig.Instance.RecordDirectory))
                                MessageBox.Show(ScreenRecorder.Properties.Resources.TheRecordingPathIsNotSet, ScreenRecorder.Properties.Resources.OpenEncodingFolderInFileExplorer, MessageBoxButton.OK, MessageBoxImage.Error);
                            else
                                MessageBox.Show(ScreenRecorder.Properties.Resources.RecordingPathDoesNotExist, ScreenRecorder.Properties.Resources.OpenEncodingFolderInFileExplorer, MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        else
                        {
                            string ext = ".";
                            string[] exts = encodeFormat.Extensions?.Split(',');
                            if (exts != null && exts.Length > 0)
                                ext += exts[0];

                            DateTime now = DateTime.Now;

                            string filePath = string.Format("{0}\\{1}-{2}{3}",
                                AppConfig.Instance.RecordDirectory,
                                AppConstants.AppName,
                                DateTime.Now.ToString("yyyyMMdd-hhmmss.fff"), ext);

                            if (!System.IO.File.Exists(filePath))
                            {
                                AppManager.Instance.ScreenEncoder.Start(encodeFormat.Format, filePath,
                                        AppConfig.Instance.SelectedRecordVideoCodec, AppConfig.Instance.SelectedRecordVideoBitrate,
                                        AppConfig.Instance.SelectedRecordAudioCodec, AppConfig.Instance.SelectedRecordAudioBitrate,
                                        System.Windows.Forms.Screen.PrimaryScreen.DeviceName);
                            }
                        }
                    }
                }
                else
                {
                    AppManager.Instance.ScreenEncoder.Resume();
                }
            }));

        public DelegateCommand PauseScreenRecordCommand => pauseScreenRecordCommand ??
            (pauseScreenRecordCommand = new DelegateCommand(o =>
            {
                if (AppManager.Instance.ScreenEncoder.Status == Encoder.EncoderStatus.Start)
                {
                    AppManager.Instance.ScreenEncoder.Pause();
                }
            }));

        public DelegateCommand StopScreenRecordCommand => stopScreenRecordCommand ??
            (stopScreenRecordCommand = new DelegateCommand(o =>
            {
                if (AppManager.Instance.ScreenEncoder.Status != Encoder.EncoderStatus.Stop)
                {
                    AppManager.Instance.ScreenEncoder.Stop();
                }
            }));

        public DelegateCommand SelectRecordDirectory => selectRecordDirectory ??
            (selectRecordDirectory = new DelegateCommand(o =>
            {
                System.Windows.Forms.FolderBrowserDialog folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
                folderBrowserDialog.Description = ScreenRecorder.Properties.Resources.SetsTheRecordingPath;
                folderBrowserDialog.SelectedPath = AppConfig.Instance.RecordDirectory;
                if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    AppConfig.Instance.RecordDirectory = folderBrowserDialog.SelectedPath;
                }
            }));

        public DelegateCommand OpenRecordDirecotryCommand => openRecordDirecotryCommand ??
            (openRecordDirecotryCommand = new DelegateCommand(o =>
            {
                try
                {
                    if (System.IO.Directory.Exists(AppConfig.Instance.RecordDirectory))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", string.Format("\"{0}\"", AppConfig.Instance.RecordDirectory));
                    }
                    else
                    {
                        
                        if (string.IsNullOrWhiteSpace(AppConfig.Instance.RecordDirectory))
                            MessageBox.Show(ScreenRecorder.Properties.Resources.TheRecordingPathIsNotSet, ScreenRecorder.Properties.Resources.OpenEncodingFolderInFileExplorer, MessageBoxButton.OK, MessageBoxImage.Error);
                        else
                            MessageBox.Show(ScreenRecorder.Properties.Resources.RecordingPathDoesNotExist, ScreenRecorder.Properties.Resources.OpenEncodingFolderInFileExplorer, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch { }
            }));
        #endregion

        public DelegateCommand OpenFolderInWindowExplorerCommand => openFolderInWindowExplorerCommand ??
            (openFolderInWindowExplorerCommand = new DelegateCommand(o =>
            {
                if (o is string folder)
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", string.Format("\"{0}\"", folder));
                    }
                    catch { }
                }
            }, o =>
            {
                if (o is string folder)
                {
                    return System.IO.Directory.Exists(folder);
                }
                return false;
            }));


    }
}
