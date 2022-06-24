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
        public static volatile Mutex Mutex = null;

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
                    VideoClockEvent.Start();
                    AppManager.Instance.Initialize();
                    AppConfig.Instance.WhenChanged(() =>
                    {
                        VideoClockEvent.Framerate = AppConfig.Instance.AdvancedSettings ?
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

        protected override void OnExit(ExitEventArgs e)
        {
            AppCommands.Instance.Dispose();
            AppConfig.Instance.Dispose();
            AppManager.Instance.Dispose();
            VideoClockEvent.Stop();

            base.OnExit(e);
        }
    }
}
