using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;

namespace ScreenRecorder
{
    /// <summary>
    /// App.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class App : Application
    {
        public volatile static Mutex Mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
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

        protected override void OnExit(ExitEventArgs e)
        {
            AppCommands.Instance.Dispose();
            AppConfig.Instance.Dispose();
            AppManager.Instance.Dispose();
            SystemClockEvent.Stop();

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
