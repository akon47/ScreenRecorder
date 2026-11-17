using System;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ScreenRecorder.Services;
using ScreenRecorder.Theming;
using ScreenRecorder.ViewModels;
using ScreenRecorder.Views;

namespace ScreenRecorder
{
    /// <summary>
    /// Application entry point and composition root: enforces a single instance, builds the
    /// service container (settings, recording, shortcuts, view models), applies the Windows
    /// theme and shows the toolbar. Nothing else in the app creates its own dependencies.
    /// </summary>
    public partial class App : Application
    {
        private Mutex _singleInstance;
        private ServiceProvider _services;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ApplyLanguageOverride(e.Args);
            ApplyThemeOverride(e.Args);

            // Before any window exists: decide GPU vs software rendering for this machine.
            RenderModeHelper.Initialize();
            RenderModeHelper.Changed += (s, a) => ThemeManager.Refresh(this);
            ThemeManager.Initialize(this);

            _singleInstance = new Mutex(true, AppConstants.AppName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                // Already running — possibly hidden in the notification area. Starting the
                // app again is the natural way to ask for it, so bring that instance up.
                if (!SignalRunningInstance())
                    MessageDialog.Show(null, ScreenRecorder.Properties.Resources.AlreadyRunning);
                Shutdown(-1);
                return;
            }

            try
            {
                _services = ConfigureServices();

                var window = _services.GetRequiredService<MainWindow>();
                MainWindow = window;
                window.Show();

                var tray = _services.GetRequiredService<TrayIconService>();
                tray.ShowRequested += (s, a) => window.RestoreFromTray();
                tray.MenuRequested += (s, a) => window.ShowTrayMenu();
                tray.Show();
                ListenForShowRequests(window);

                // Opening an encoder to see whether it works takes a moment: off the UI thread.
                _ = _services.GetRequiredService<RecordingService>().DetectHardwareEncodersAsync();
            }
            catch (Exception ex)
            {
                MessageDialog.Show(null, ex.Message);
                Shutdown(-10);
            }
        }

        #region Second launch → show the running instance

        private const string ShowEventName = AppConstants.AppName + ".Show";

        private EventWaitHandle _showEvent;
        private RegisteredWaitHandle _showWait;

        /// <summary>Asks the instance that is already running to show its toolbar.</summary>
        private static bool SignalRunningInstance()
        {
            try
            {
                using EventWaitHandle running = EventWaitHandle.OpenExisting(ShowEventName);
                return running.Set();
            }
            catch
            {
                return false; // that instance is still starting up or is shutting down
            }
        }

        private void ListenForShowRequests(MainWindow window)
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _showWait = ThreadPool.RegisterWaitForSingleObject(
                _showEvent,
                (state, timedOut) => Dispatcher.BeginInvoke(new Action(window.RestoreFromTray)),
                null, Timeout.Infinite, executeOnlyOnce: false);
        }

        #endregion

        /// <summary>
        /// <c>--lang &lt;culture&gt;</c> (e.g. <c>--lang ko-KR</c>, <c>--lang en</c>) shows the UI in
        /// that language instead of the Windows display language.
        /// </summary>
        private static void ApplyLanguageOverride(string[] args)
        {
            int index = Array.FindIndex(args, a => string.Equals(a, "--lang", StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index + 1 >= args.Length)
                return;

            try
            {
                var culture = new System.Globalization.CultureInfo(args[index + 1]);
                Thread.CurrentThread.CurrentUICulture = culture;
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
                ScreenRecorder.Properties.Resources.Culture = culture;
            }
            catch (System.Globalization.CultureNotFoundException)
            {
                // Unknown culture name: keep the system language.
            }
        }

        /// <summary><c>--theme light|dark</c> uses that theme instead of the Windows app mode.</summary>
        private static void ApplyThemeOverride(string[] args)
        {
            int index = Array.FindIndex(args, a => string.Equals(a, "--theme", StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index + 1 < args.Length && Enum.TryParse(args[index + 1], ignoreCase: true, out AppTheme theme))
                ThemeManager.Override = theme;
        }

        private static ServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton(_ => new SettingsStore(AppConstants.AppDataFolderPath));
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<CaptureExclusionService>();
            services.AddSingleton<ClickHighlightService>();
            services.AddSingleton<ShortcutService>();
            services.AddSingleton(sp => new RecordingService(
                sp.GetRequiredService<SettingsStore>().Settings,
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<CaptureExclusionService>(),
                sp.GetRequiredService<ClickHighlightService>()));

            services.AddSingleton<TrayIconService>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            return services.BuildServiceProvider();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Disposes the singletons in reverse order of creation: view models, the recording
            // service (finalizing a recording still in progress), shortcuts, then the settings
            // store (flushing pending changes).
            _showWait?.Unregister(null);
            _showEvent?.Dispose();
            _services?.Dispose();
            _singleInstance?.Dispose();

            base.OnExit(e);
        }
    }
}
