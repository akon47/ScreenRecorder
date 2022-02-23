using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Diagnostics;
using System.Windows.Input;
using ScreenRecorder.Command;
using ScreenRecorder.Config;
using ScreenRecorder.Encoder;
using ScreenRecorder.DirectX;

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

            // then use globalhotkey
            //EventManager.RegisterClassHandler(typeof(Window), System.Windows.Input.Keyboard.PreviewKeyDownEvent, new KeyEventHandler(PreviewKeyDown), true);

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

        #region Private Command Fields
        private DelegateCommand startScreenRecordCommand;
        private DelegateCommand pauseScreenRecordCommand;
        private DelegateCommand stopScreenRecordCommand;
        private DelegateCommand selectRegionCommand;
        private DelegateCommand openFolderInWindowExplorerCommand;
        private DelegateCommand openRecordDirecotryCommand;
        private DelegateCommand selectRecordDirectory;

        private DelegateCommand openShortcutSettingsCommand;

        private DelegateCommand windowCloseCommand;
        private DelegateCommand windowMinimizeCommand;
        #endregion

        private Rect? SelectDisplayAndRect (object o)
        {
            var region = new Rect(0, 0, double.MaxValue, double.MaxValue);
            var regionSelectorWindow = new Region.RegionSelectorWindow()
            {
                RegionSelectionMode = AppConfig.Instance.RegionSelectionMode
            };
            try
            {
                if (!regionSelectorWindow.ShowDialog().Value)
                    return null;

                var result = regionSelectorWindow.RegionSelectionResult;
                if (result != null)
                {
                    string displayDeviceName = result.DeviceName;
                    var monitorInfo = MonitorInfo.GetMonitorInfo(displayDeviceName);
                    Debug.Assert(monitorInfo != null);
                    region = result.Region;

                    if (AppConfig.Instance.ForceSourceSize && result.Hwnd != IntPtr.Zero)
                    {
                        int cx = AppConfig.Instance.ForcedSourceWidth;
                        int cy = AppConfig.Instance.ForcedSourceHeight;
                        // sichern gerader Zahlen ?
                        cx &= ~0x01;
                        cy &= ~0x01;

                        region.Width = cx;
                        region.Height = cy;

                        // rect size check with coordinates relative to according screen
                        Rect monitorBounds = monitorInfo.Bounds;
                        monitorBounds.Offset(-monitorInfo.Left, -monitorInfo.Top);
                        double offsetX = (region.Right > monitorBounds.Right) ? monitorBounds.Right - region.Right : 0;
                        double offsetY = (region.Bottom > monitorBounds.Bottom) ? monitorBounds.Bottom - region.Bottom : 0;
                        region.Offset(offsetX, offsetY);
                        // change window size on display with absolute coordinates
                        region.Offset(monitorInfo.Left, monitorInfo.Top);
                        Region.WindowRegion.SizeWindow(result.Hwnd, (int)(region.Left), (int)(region.Top), cx, cy, true);
                        // get absolute coordinates after resizing
                        region = Region.WindowRegion.GetWindowRectangle(result.Hwnd);
                        // shift absolute coordinates relative to screen with 0,0 as left,top
                        region.Offset(-monitorInfo.Left, -monitorInfo.Top);
                    }
                    else
                    {
                        // Ensure even numbers ?
                        region.Width = ((int)region.Width) & (~0x01);
                        region.Height = ((int)region.Height) & (~0x01);
                    }

                    if (region.Width < 100 || region.Height < 100)
                    {
                        MessageBox.Show(ScreenRecorder.Properties.Resources.RegionSizeError,
                            AppConstants.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                        return null;
                    }

                    AppConfig.Instance.ScreenCaptureRect = region;
                    AppConfig.Instance.ScreenCaptureMonitor = displayDeviceName;
                    AppManager.Instance.ScreenCaptureMonitorDescription = monitorInfo.Description;
                }
            }
            finally
            {
                AppConfig.Instance.RegionSelectionMode = regionSelectorWindow.RegionSelectionMode;
            }

            return region;
        }

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

                                if (!AppConfig.Instance.ScreenCaptureRect.HasValue || AppConfig.Instance.ScreenCaptureRect.Value.IsEmpty)
                                {
                                    // select ScreenCaptureRect and ScreenCaptureMonitor, update ScreenCaptureMonitorDescription
                                    if (!SelectDisplayAndRect(o).HasValue)
                                    {
                                        MessageBox.Show("Unknown capture rect", AppConstants.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                                        return;
                                    }
                                }

                                MonitorInfo monitorInfo = MonitorInfo.GetMonitorInfo(AppConfig.Instance.ScreenCaptureMonitor);
                                if (monitorInfo == null)
                                    throw new ArgumentException($"{AppConfig.Instance.ScreenCaptureMonitor} does not exist");

                                // securety check (coordinates relative to selected monitor with 0,0 as left,top)
                                Rect monitorRegion = new Rect(0, 0, monitorInfo.Width, monitorInfo.Height);
                                AppConfig.Instance.ScreenCaptureRect = Rect.Intersect(AppConfig.Instance.ScreenCaptureRect.Value, monitorRegion);

                                // Start Record
                                try
                                {
                                    DateTime? captureStart = AppConfig.Instance.CaptureTimeControlled ? AppConfig.Instance.CaptureStartTime : (DateTime?)null;
                                    DateTime? captureEnd = AppConfig.Instance.CaptureTimeControlled ? AppConfig.Instance.CaptureEndTime : (DateTime?)null;

                                    AppManager.Instance.ScreenEncoder.Start(encodeFormat.Format, filePath,
                                            videoCodec, AppConfig.Instance.SelectedRecordVideoBitrate,
                                            audioCodec, AppConfig.Instance.SelectedRecordAudioBitrate,
                                            monitorInfo.DeviceName, AppConfig.Instance.ScreenCaptureRect.Value,
                                            AppConfig.Instance.ScreenCaptureCursorVisible,
                                            AppConfig.Instance.RecordMicrophone,
                                            captureStart, captureEnd
                                            );
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

        public DelegateCommand PauseScreenRecordCommand => pauseScreenRecordCommand ??
            (pauseScreenRecordCommand = new DelegateCommand(o =>
            {
                if (AppManager.Instance.ScreenEncoder.Status == Encoder.EncoderStatus.Wait)
                {
                    // prevent closing of program for manual stop of time controlled capture
                    AppManager.Instance.ScreenEncoder.StopByAutomatic = false;
                    AppManager.Instance.ScreenEncoder.Stop();
                }
                else if (AppManager.Instance.ScreenEncoder.Status == Encoder.EncoderStatus.Start)
                {
                    AppManager.Instance.ScreenEncoder.Pause();
                }
            }));

        public DelegateCommand StopScreenRecordCommand => stopScreenRecordCommand ??
            (stopScreenRecordCommand = new DelegateCommand(o =>
            {
                if (AppManager.Instance.ScreenEncoder.Status != Encoder.EncoderStatus.Stop)
                {
                    // prevent closing of program for manual stop of time controlled capture
                    AppManager.Instance.ScreenEncoder.StopByAutomatic = false;
                    AppManager.Instance.ScreenEncoder.Stop();
                }
            }));

        public DelegateCommand SelectRegionCommand => selectRegionCommand ??
            (selectRegionCommand = new DelegateCommand(o =>
            {
                if (AppManager.Instance.ScreenEncoder.Status != Encoder.EncoderStatus.Stop)
                    return;

                AppManager.Instance.RecordSettingChecked = false;
                SelectDisplayAndRect(o);
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

        #region Common Commands
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
        #endregion

        #region Shortcut Commands
        public DelegateCommand OpenShortcutSettingsCommand => openShortcutSettingsCommand ??
            (openShortcutSettingsCommand = new DelegateCommand(o =>
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

        public DelegateCommand WindowCloseCommand => windowCloseCommand ??
            (windowCloseCommand = new DelegateCommand(o =>
            {
                if (o is Window window)
                {
                    window.Close();
                }
            }, o =>
            {
                return o is Window;
            }));

        public DelegateCommand WindowMinimizeCommand => windowMinimizeCommand ??
            (windowMinimizeCommand = new DelegateCommand(o =>
            {
                var mainWnd = Application.Current.MainWindow;
                if (Application.Current is App myApp)
                {
                    myApp.NotifyIcon.Visible = true;
                    mainWnd.Hide();
                }
                else
                    SystemCommands.MinimizeWindow(mainWnd);
            }));

        #endregion
    }
}
