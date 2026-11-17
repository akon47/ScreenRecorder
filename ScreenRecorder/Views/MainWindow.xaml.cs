using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ScreenRecorder.Services;
using ScreenRecorder.ViewModels;

namespace ScreenRecorder.Views
{
    /// <summary>
    /// The toolbar window. Code-behind holds only what is genuinely about the window: its
    /// position (restored, kept on screen, snapped to screen edges while dragging), the
    /// settings flyout it anchors, and staying out of its own recordings.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int WmWindowPosChanging = 0x0046;

        /// <summary>Distance (pixels) within which the window snaps to a work-area edge.</summary>
        private const int SnapDistance = 15;

        private readonly MainViewModel _viewModel;
        private readonly AppSettings _settings;
        private readonly CaptureExclusionService _exclusion;
        private SettingsFlyout _flyout;
        private DateTime _flyoutClosedAt = DateTime.MinValue;
        private readonly TrayIconService _tray;

        public MainWindow(MainViewModel viewModel, SettingsStore store, CaptureExclusionService exclusion, TrayIconService tray = null)
        {
            _tray = tray;
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _settings = store?.Settings ?? throw new ArgumentNullException(nameof(store));
            _exclusion = exclusion ?? throw new ArgumentNullException(nameof(exclusion));

            InitializeComponent();
            DataContext = viewModel;

            RestorePosition();
            exclusion.Register(this);

            // No tooltips while recording: they are separate popup windows and would end up
            // in the picture.
            AddHandler(ToolTipOpeningEvent, new ToolTipEventHandler(OnToolTipOpening));
        }

        #region Position

        private void RestorePosition()
        {
            var workArea = SystemParameters.WorkArea;
            double left = _settings.WindowLeft;
            double top = _settings.WindowTop;

            if (left < 0 || top < 0 || double.IsNaN(left) || double.IsNaN(top))
            {
                // First run: top-right corner of the primary work area.
                Loaded += (s, e) =>
                {
                    Left = Math.Max(workArea.Left, workArea.Right - ActualWidth - 24);
                    Top = workArea.Top + 24;
                };
                return;
            }

            Left = left;
            Top = top;
            Loaded += (s, e) => EnsureOnScreen();
        }

        /// <summary>Pulls the window back when its saved position is on a display that is gone.</summary>
        private void EnsureOnScreen()
        {
            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

            if (Left + ActualWidth > virtualRight) Left = virtualRight - ActualWidth;
            if (Top + ActualHeight > virtualBottom) Top = virtualBottom - ActualHeight;
            if (Left < virtualLeft) Left = virtualLeft;
            if (Top < virtualTop) Top = virtualTop;
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);

            if (WindowState == WindowState.Normal && IsLoaded)
            {
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
            }

            _flyout?.Close();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ((HwndSource)PresentationSource.FromVisual(this)).AddHook(WndProc);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPos
        {
            public IntPtr Hwnd;
            public IntPtr HwndInsertAfter;
            public int X;
            public int Y;
            public int Cx;
            public int Cy;
            public int Flags;
        }

        /// <summary>Snaps the window to the edges of the work area while it is being moved.</summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmWindowPosChanging)
                return IntPtr.Zero;

            var position = Marshal.PtrToStructure<WindowPos>(lParam);

            // Only a real move carries a position; x/y (and cx/cy) are meaningless when the
            // corresponding "no change" flag is set.
            if ((position.Flags & SwpNoMove) != 0)
                return IntPtr.Zero;

            int width = position.Cx;
            int height = position.Cy;
            if ((position.Flags & SwpNoSize) != 0)
            {
                if (!GetWindowRect(hwnd, out NativeRect rect))
                    return IntPtr.Zero;

                width = rect.Right - rect.Left;
                height = rect.Bottom - rect.Top;
            }

            var workArea = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;

            if (Math.Abs(position.X - workArea.Left) <= SnapDistance)
                position.X = workArea.Left;
            if (Math.Abs(position.Y - workArea.Top) <= SnapDistance)
                position.Y = workArea.Top;
            if (Math.Abs(position.X + width - workArea.Right) <= SnapDistance)
                position.X = workArea.Right - width;
            if (Math.Abs(position.Y + height - workArea.Bottom) <= SnapDistance)
                position.Y = workArea.Bottom - height;

            Marshal.StructureToPtr(position, lParam, false);
            return IntPtr.Zero;
        }

        private const int SwpNoSize = 0x0001;
        private const int SwpNoMove = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

        #endregion

        #region Settings flyout

        private void SettingsButton_Checked(object sender, RoutedEventArgs e)
        {
            // Clicking the gear while the flyout is open deactivates (and so closes) the flyout
            // first; that same click must not immediately reopen it.
            if ((DateTime.UtcNow - _flyoutClosedAt).TotalMilliseconds < 250)
            {
                SettingsButton.IsChecked = false;
                return;
            }

            OpenFlyout();
        }

        private void SettingsButton_Unchecked(object sender, RoutedEventArgs e) => _flyout?.Close();

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e) => SettingsButton.IsChecked = true;

        private void OpenFlyout()
        {
            if (_flyout != null)
                return;

            _viewModel.SettingsViewModel.Refresh();

            _flyout = new SettingsFlyout(_viewModel.SettingsViewModel, _viewModel.OpenShortcutSettingsCommand) { Owner = this };
            _exclusion.Register(_flyout);
            _flyout.Closed += (s, e) =>
            {
                _flyout = null;
                _flyoutClosedAt = DateTime.UtcNow;
                SettingsButton.IsChecked = false;
            };

            _flyout.ShowBelow(this);
        }

        #endregion

        private void OnToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (_viewModel.Recording.IsRecording && _exclusion.IsActive)
                e.Handled = true;
        }

        #region Notification area

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => HideToTray();

        /// <summary>
        /// Minimizing — by the button or by Windows (Win+D, "show desktop") — hides the toolbar
        /// into the notification area. The global shortcuts keep working, so recording does not
        /// need the window; while hidden it is also out of every recording.
        /// </summary>
        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (WindowState == WindowState.Minimized)
            {
                // Not from inside the state change itself.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    WindowState = WindowState.Normal;
                    HideToTray();
                }));
            }
        }

        public void HideToTray()
        {
            _flyout?.Close();
            Hide();
            _tray?.NotifyHiddenOnce();
        }

        /// <summary>Shows the toolbar again and gives it the focus.</summary>
        public void RestoreFromTray()
        {
            if (!IsVisible)
                Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }

        /// <summary>Opens the app menu at the mouse position (right-click on the tray icon).</summary>
        public void ShowTrayMenu()
        {
            ContextMenu menu = ContextMenu;
            if (menu == null || menu.IsOpen)
                return;

            // A menu only closes on a click elsewhere when its owner is the foreground window.
            SetForegroundWindow(new WindowInteropHelper(this).Handle);

            ShowToolbarMenuItem.Visibility = IsVisible ? Visibility.Collapsed : Visibility.Visible;
            ShowToolbarSeparator.Visibility = ShowToolbarMenuItem.Visibility;
            menu.PlacementTarget = this;
            menu.IsOpen = true;
        }

        private void ContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            // Only the tray menu offers "show the toolbar"; on the toolbar itself it is noise.
            ShowToolbarMenuItem.Visibility = Visibility.Collapsed;
            ShowToolbarSeparator.Visibility = Visibility.Collapsed;
        }

        private void ShowToolbarMenuItem_Click(object sender, RoutedEventArgs e) => RestoreFromTray();

        /// <summary>Lists the windows that are open right now.</summary>
        private void RecordWindowMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, RecordWindowMenuItem))
                return;

            RecordWindowMenuItem.Items.Clear();
            foreach (Region.WindowListItem window in Region.WindowList.GetWindows())
            {
                RecordWindowMenuItem.Items.Add(new MenuItem
                {
                    // A TextBlock, not a string: an underscore in a title is not an access key.
                    Header = new TextBlock { Text = window.Title },
                    Icon = window.Icon == null ? null : new Image { Source = window.Icon, Width = 16, Height = 16 },
                    Command = _viewModel.RecordWindowCommand,
                    CommandParameter = window.Handle,
                });
            }

            if (RecordWindowMenuItem.Items.Count == 0)
                RecordWindowMenuItem.Items.Add(new MenuItem { Header = Properties.Resources.NoWindowsToRecord, IsEnabled = false });
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        #endregion

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            _flyout?.Close();
            base.OnClosing(e);
        }
    }
}
