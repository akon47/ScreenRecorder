using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CustomConverter;
using ScreenRecorder.Theming;

namespace ScreenRecorder.Views
{
    /// <summary>
    /// A transient notification in the bottom-right corner of the primary work area. It never
    /// takes focus, closes by itself after a few seconds, and at most one is shown at a time.
    /// </summary>
    public partial class ToastWindow : Window
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);
        private const double ScreenMargin = 16;

        private static ToastWindow _current;

        private readonly Action _onClick;
        private readonly DispatcherTimer _timer;

        private ToastWindow(string title, string detail, Action onClick)
        {
            InitializeComponent();

            FileNameText.Text = title;
            DetailText.Text = detail;
            DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
            _onClick = onClick;

            _timer = new DispatcherTimer { Interval = Lifetime };
            _timer.Tick += (s, e) => Close();
        }

        /// <summary>Announces that <paramref name="filePath"/> was saved; clicking runs <paramref name="onClick"/>.</summary>
        public static void ShowSaved(string filePath, Action onClick)
        {
            string size = null;
            try
            {
                size = TimeFormatter.FormatBytes(new FileInfo(filePath).Length);
            }
            catch
            {
            }

            _current?.Close();
            _current = new ToastWindow(Path.GetFileName(filePath), size, onClick);
            _current.Show();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Never activate (do not steal focus from what the user is doing) and stay out of Alt+Tab.
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
                new IntPtr(style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - ScreenMargin;
            Top = workArea.Bottom - ActualHeight - ScreenMargin;
            _timer.Start();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            try
            {
                _onClick?.Invoke();
            }
            finally
            {
                Close();
            }
        }

        // Keep it up while the pointer rests on it.
        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            _timer.Stop();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _timer.Start();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            if (ReferenceEquals(_current, this))
                _current = null;
            base.OnClosed(e);
        }
    }
}
