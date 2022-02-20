using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Diagnostics;

namespace ScreenRecorder
{
    /// <summary>
    /// App.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class App : Application
    {
        public volatile static Mutex Mutex = null;
        public System.Windows.Forms.NotifyIcon NotifyIcon { get; private set; }

        public App()
        {
            NotifyIcon = new System.Windows.Forms.NotifyIcon();
            NotifyIcon.Visible = false;
            NotifyIcon.Click += OnNotifyIconClick;
            NotifyIcon.Text = AppConstants.AppName;
            var iconUri = new Uri("/icon.ico", UriKind.Relative);
            using (var iconStream = Application.GetResourceStream(iconUri).Stream)
            {
                NotifyIcon.Icon = new System.Drawing.Icon(iconStream);
            }
        }

        void OnNotifyIconClick(object sender, EventArgs e)
        {
            // restore program from system tray to desktop
            MainWindow.Visibility = Visibility.Visible;
            MainWindow.WindowState = WindowState.Normal;
            NotifyIcon.Visible = false;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
#if DEBUG
            ScreenRecorder.Properties.Resources.Culture = new System.Globalization.CultureInfo("en-US");
#endif
            try
            {
                Mutex = new Mutex(true, AppConstants.AppName, out bool isNew);
                if (isNew)
                {
                    if (!IsMicrosoftVisualCPlusPlus2019OrNewerAvailable())
                    {
                        MessageBox.Show("Please Install \"Microsoft Visual C++ 2019 or newer Redistributable (x64)\"");
                        Environment.Exit(-2);
                    }

                    SystemClockEvent.Start();
                    AppManager.Instance.Initialize();
                    AppManager.Instance.PropertyChanged += Instance_PropertyChanged;
                    AppConfig.Instance.WhenChanged(() =>
                    {
                        SystemClockEvent.Framerate = AppConfig.Instance.AdvancedSettings ? 
                            AppConfig.Instance.SelectedRecordFrameRate : 60;
                    },
                    nameof(AppConfig.SelectedRecordFrameRate),
                    nameof(AppConfig.AdvancedSettings));

                    base.OnStartup(e);
                }
                else
                {
                    MessageBox.Show("The program is already running", "", MessageBoxButton.OK, MessageBoxImage.Error);
                    Environment.Exit(-1);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(-10);
            }
        }

        private void Instance_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName.Equals("IsStarted")
                || e.PropertyName.Equals("IsStartedWithEncode")
                || e.PropertyName.Equals("IsPaused")
                || e.PropertyName.Equals("IsStopped")
                )
            {
                Debug.WriteLine($"{e.PropertyName}: isStart: {AppManager.Instance.ScreenEncoder.IsStarted}, isEncode:{AppManager.Instance.ScreenEncoder.IsStartedWithEncode}");
                NotifyIcon.Text = AppConstants.AppName;
                if (AppManager.Instance.ScreenEncoder.IsStarted && !AppManager.Instance.ScreenEncoder.IsStartedWithEncode)
                    NotifyIcon.Text += " waiting for start ...";
                else if (AppManager.Instance.ScreenEncoder.IsStarted && AppManager.Instance.ScreenEncoder.IsStartedWithEncode)
                    NotifyIcon.Text += " recording ...";
                else if (AppManager.Instance.ScreenEncoder.IsPaused)
                    NotifyIcon.Text += " paused ...";
                else if (AppManager.Instance.ScreenEncoder.IsStopped)
                    NotifyIcon.Text += " stopped ...";
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppCommands.Instance.Dispose();
            AppConfig.Instance.Dispose();
            AppManager.Instance.Dispose();
            SystemClockEvent.Stop();
            
            // maybe not neccessary
            NotifyIcon.Click -= OnNotifyIconClick;
            NotifyIcon.Visible = false;
            NotifyIcon.Dispose();

            base.OnExit(e);
        }

        private bool IsMicrosoftVisualCPlusPlus2019OrNewerAvailable()
        {
            using (var depRegistryKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"Installer\Dependencies", false))
            {
                foreach (string subKeyName in depRegistryKey.GetSubKeyNames())
                {
                    using (var registryKey = depRegistryKey.OpenSubKey(subKeyName))
                    {
                        if (registryKey.GetValue("DisplayName") is string displayName && Regex.IsMatch(displayName, "[cC]\\+\\+.*(?:2019|2022).*[xX]64"))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
