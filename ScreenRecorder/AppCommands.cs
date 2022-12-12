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

        private static volatile AppCommands _instance;
        private static readonly object _syncRoot = new object();

        public static AppCommands Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_syncRoot)
                    {
                        if (_instance == null)
                        {
                            _instance = new AppCommands();
                        }
                    }
                }

                return _instance;
            }
        }

        private readonly string _configFilePath = System.IO.Path.Combine(AppConstants.AppDataFolderPath, "commands");
        private ConfigFileSaveWorker _configFileSaveWorker;
        private volatile bool _isDisposed = false;

        private AppCommands()
        {
            Load(_configFilePath);

            foreach (var propertyInfo in this.GetType().GetProperties())
            {
                if (propertyInfo.PropertyType == typeof(DelegateCommand))
                {
                    var command = propertyInfo.GetValue(this, null) as DelegateCommand;
                    if (command != null)
                    {
                        command.WhenChanged(() =>
                        {
                            _configFileSaveWorker?.SetModifiedConfigData();
                        },
                        nameof(DelegateCommand.KeyGesture));
                    }
                }
            }

            // then use globalhotkey
            //EventManager.RegisterClassHandler(typeof(Window), System.Windows.Input.Keyboard.PreviewKeyDownEvent, new KeyEventHandler(PreviewKeyDown), true);

            _configFileSaveWorker = new ConfigFileSaveWorker(this, _configFilePath);
        }

        private void PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            foreach (var propertyInfo in this.GetType().GetProperties())
            {
                if (propertyInfo.PropertyType == typeof(DelegateCommand))
                {
                    var command = propertyInfo.GetValue(this, null) as DelegateCommand;
                    if (command?.KeyGesture != null)
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
            if (_isDisposed)
                return;

            _configFileSaveWorker?.Dispose();
            _configFileSaveWorker = null;

            _isDisposed = true;
        }

        #endregion

        #region Private Command Fields
        private DelegateCommand _startScreenRecordCommand;
        private DelegateCommand _pauseScreenRecordCommand;
        private DelegateCommand _stopScreenRecordCommand;
        private DelegateCommand _openFolderInWindowExplorerCommand;
        private DelegateCommand _openRecordDirecotryCommand;
        private DelegateCommand _selectRecordDirectory;

        private DelegateCommand _openShortcutSettingsCommand;

        private DelegateCommand _windowCloseCommand;
        #endregion

        #region Record Commands
        public DelegateCommand StartScreenRecordCommand => _startScreenRecordCommand ??
            (_startScreenRecordCommand = new DelegateCommand(o =>
            {
                if (AppManager.Instance.ScreenEncoder.Status == Encoder.EncoderStatus.Stop)
                {
                    EncoderFormat encodeFormat = AppManager.Instance.EncoderFormats.First((x => x.Format.Equals(AppConfig.Instance.SelectedRecordFormat, StringComparison.OrdinalIgnoreCase)));
                    if (encodeFormat != null)
                    {
                        if (!System.IO.Directory.Exists(AppConfig.Instance.RecordDirectory))
                        {
                            if (string.IsNullOrWhiteSpace(AppConfig.Instance.RecordDirectory))
                                MessageBox.Show(ScreenRecorder.Properties.Resources.TheRecordingPathIsNotSet,
                                    ScreenRecorder.Properties.Resources.OpenEncodingFolderInFileExplorer,
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            else
                                MessageBox.Show(ScreenRecorder.Properties.Resources.RecordingPathDoesNotExist,
                                    ScreenRecorder.Properties.Resources.OpenEncodingFolderInFileExplorer,
                                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                                DateTime.Now.ToString("yyyyMMdd-HHmmss.fff"), ext);

                            if (!System.IO.File.Exists(filePath))
                            {
                                /// Only when the Advanced Settings menu is enabled, the settings in the Advanced Settings apply.
                                var videoCodec = AppConfig.Instance.AdvancedSettings ?
                                    AppConfig.Instance.SelectedRecordVideoCodec : MediaEncoder.VideoCodec.H264;
                                var audioCodec = AppConfig.Instance.AdvancedSettings ?
                                    AppConfig.Instance.SelectedRecordAudioCodec : MediaEncoder.AudioCodec.Aac;
                                var displayDeviceName = o is string target ? target : AppConfig.Instance.ScreenCaptureMonitor;
                                var region = new Rect(0, 0, double.MaxValue, double.MaxValue);

                                switch(displayDeviceName)
                                {
                                    case CaptureTarget.PrimaryCaptureTargetDeviceName:
                                        #region Select PrimayDisplay
                                        displayDeviceName = System.Windows.Forms.Screen.PrimaryScreen.DeviceName;
                                        #endregion
                                        break;
                                    case CaptureTarget.ByUserChoiceTargetDeviceName:
                                        #region Select Region
                                        var regionSelectorWindow = new Region.RegionSelectorWindow()
                                        {
                                            RegionSelectionMode = AppConfig.Instance.RegionSelectionMode
                                        };
                                        try
                                        {
                                            if (!regionSelectorWindow.ShowDialog().Value)
                                            {
                                                return;
                                            }

                                            var result = regionSelectorWindow.RegionSelectionResult;
                                            if (result != null)
                                            {
                                                displayDeviceName = result.DeviceName;
                                                region = result.Region;

                                                region.Width = ((int)region.Width) & (~0x01);
                                                region.Height = ((int)region.Height) & (~0x01);

                                                if(region.Width < 100 || region.Height < 100)
                                                {
                                                    MessageBox.Show(ScreenRecorder.Properties.Resources.RegionSizeError,
                                                        AppConstants.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                                                    return;
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            AppConfig.Instance.RegionSelectionMode = regionSelectorWindow.RegionSelectionMode;
                                        }
                                        break;
                                        #endregion
                                }

                                // Start Record
                                try
                                {
                                    AppManager.Instance.ScreenEncoder.Start(encodeFormat.Format, filePath,
                                            videoCodec, AppConfig.Instance.SelectedRecordVideoBitrate,
                                            audioCodec, AppConfig.Instance.SelectedRecordAudioBitrate,
                                            displayDeviceName, region,
                                            AppConfig.Instance.ScreenCaptureCursorVisible,
                                            AppConfig.Instance.RecordMicrophone);
                                }
                                catch
                                {
                                    MessageBox.Show(ScreenRecorder.Properties.Resources.FailedToStartRecording,
                                        AppConstants.AppName,
                                        MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                            }
                        }
                    }
                }
                else
                {
                    AppManager.Instance.ScreenEncoder.Resume();
                }
            }));

        public DelegateCommand PauseScreenRecordCommand => _pauseScreenRecordCommand ??
            (_pauseScreenRecordCommand = new DelegateCommand(o =>
            {
                if (AppManager.Instance.ScreenEncoder.Status == Encoder.EncoderStatus.Start)
                {
                    AppManager.Instance.ScreenEncoder.Pause();
                }
            }));

        public DelegateCommand StopScreenRecordCommand => _stopScreenRecordCommand ??
            (_stopScreenRecordCommand = new DelegateCommand(o =>
            {
                if (AppManager.Instance.ScreenEncoder.Status != Encoder.EncoderStatus.Stop)
                {
                    AppManager.Instance.ScreenEncoder.Stop();
                }
            }));

        public DelegateCommand SelectRecordDirectory => _selectRecordDirectory ??
            (_selectRecordDirectory = new DelegateCommand(o =>
            {
                System.Windows.Forms.FolderBrowserDialog folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
                folderBrowserDialog.Description = ScreenRecorder.Properties.Resources.SetsTheRecordingPath;
                folderBrowserDialog.SelectedPath = AppConfig.Instance.RecordDirectory;
                if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    AppConfig.Instance.RecordDirectory = folderBrowserDialog.SelectedPath;
                }
            }));

        public DelegateCommand OpenRecordDirecotryCommand => _openRecordDirecotryCommand ??
            (_openRecordDirecotryCommand = new DelegateCommand(o =>
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

        #region Common Commands
        public DelegateCommand OpenFolderInWindowExplorerCommand => _openFolderInWindowExplorerCommand ??
            (_openFolderInWindowExplorerCommand = new DelegateCommand(o =>
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
        #endregion

        #region Shortcut Commands
        public DelegateCommand OpenShortcutSettingsCommand => _openShortcutSettingsCommand ??
            (_openShortcutSettingsCommand = new DelegateCommand(o =>
            {
                try
                {
                    Shortcut.GlobalHotKey.PassthroughGlobalHotKey = true;
                    Shortcut.ShortcutEditorWindow shortcutEditorWindow = new Shortcut.ShortcutEditorWindow();
                    shortcutEditorWindow.Owner = Application.Current.MainWindow;
                    shortcutEditorWindow.ShowDialog();
                }
                finally
                {
                    Shortcut.GlobalHotKey.PassthroughGlobalHotKey = false;
                }
            }));
        #endregion

        #region Window Commands

        public DelegateCommand WindowCloseCommand => _windowCloseCommand ??
            (_windowCloseCommand = new DelegateCommand(o =>
            {
                if (o is Window window)
                {
                    window.Close();
                }
            }, o => o is Window));

        #endregion
    }
}
