using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace ScreenRecorder.Services
{
    /// <summary>
    /// The notification-area ("tray") icon. The global shortcuts make the toolbar optional, so
    /// minimizing hides it here instead of leaving it on the taskbar: the icon shows that the
    /// recorder is still running and what it is doing (a red dot while recording, amber while
    /// paused), brings the toolbar back on a click and offers the app's menu on a right-click.
    ///
    /// This class only owns the icon; what a click does is decided by whoever listens to
    /// <see cref="ShowRequested"/> and <see cref="MenuRequested"/>.
    /// </summary>
    public sealed class TrayIconService : IDisposable
    {
        private readonly RecordingService _recording;
        private readonly AppSettings _settings;

        private NotifyIcon _notifyIcon;
        private Icon _idleIcon;
        private Icon _recordingIcon;
        private Icon _pausedIcon;

        /// <summary>The icon was clicked: show the toolbar.</summary>
        public event EventHandler ShowRequested;

        /// <summary>The icon was right-clicked: show the menu at the mouse position.</summary>
        public event EventHandler MenuRequested;

        public TrayIconService(RecordingService recording, SettingsStore store)
        {
            _recording = recording ?? throw new ArgumentNullException(nameof(recording));
            _settings = store?.Settings ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>Puts the icon into the notification area (once). UI thread.</summary>
        public void Show()
        {
            if (_notifyIcon != null)
                return;

            _idleIcon = LoadAppIcon();
            _recordingIcon = WithBadge(_idleIcon, Color.FromArgb(0xE8, 0x11, 0x23));
            _pausedIcon = WithBadge(_idleIcon, Color.FromArgb(0xFF, 0xB9, 0x00));

            _notifyIcon = new NotifyIcon { Visible = true };
            _notifyIcon.MouseClick += OnMouseClick;
            _recording.PropertyChanged += OnRecordingChanged;
            Update();
        }

        /// <summary>
        /// Tells the user — the first time only — where the toolbar went. Windows puts new
        /// icons into the hidden part of the notification area, so without this a minimized
        /// recorder would simply seem to have closed.
        /// </summary>
        public void NotifyHiddenOnce()
        {
            if (_notifyIcon == null || _settings.TrayHintShown)
                return;

            _settings.TrayHintShown = true;
            try
            {
                _notifyIcon.ShowBalloonTip(5000, AppConstants.AppDisplayName, Properties.Resources.TrayHint, ToolTipIcon.None);
            }
            catch
            {
                // Notifications can be switched off; the icon is still there.
            }
        }

        /// <summary>The icon's tooltip: the app name and what it is doing.</summary>
        public static string DescribeState(RecordingState state)
        {
            string status;
            switch (state)
            {
                case RecordingState.Recording: status = Properties.Resources.EncoderStatusStartMessage; break;
                case RecordingState.Paused: status = Properties.Resources.EncoderStatusPauseMessage; break;
                case RecordingState.Countdown: status = Properties.Resources.EncoderStatusCountdownMessage; break;
                default: status = Properties.Resources.EncoderStatusStopMessage; break;
            }

            return AppConstants.AppDisplayName + " — " + status;
        }

        private void OnRecordingChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RecordingService.State))
                Update();
        }

        private void Update()
        {
            if (_notifyIcon == null)
                return;

            RecordingState state = _recording.State;
            _notifyIcon.Icon = state == RecordingState.Recording ? _recordingIcon
                : state == RecordingState.Paused ? _pausedIcon
                : _idleIcon;
            _notifyIcon.Text = DescribeState(state);
        }

        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                ShowRequested?.Invoke(this, EventArgs.Empty);
            else if (e.Button == MouseButtons.Right)
                MenuRequested?.Invoke(this, EventArgs.Empty);
        }

        private static Icon LoadAppIcon()
        {
            var uri = new Uri("pack://application:,,,/ScreenRecorder;component/icon.ico", UriKind.Absolute);
            using var stream = Application.GetResourceStream(uri).Stream;
            return new Icon(stream, SystemInformation.SmallIconSize);
        }

        /// <summary>The icon with a colored dot in its lower right corner.</summary>
        internal static Icon WithBadge(Icon source, Color color)
        {
            int size = source.Width;
            using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawIcon(source, new Rectangle(0, 0, size, size));

                float diameter = size * 0.5f;
                float offset = size - diameter - 0.5f;
                using var ring = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
                using var dot = new SolidBrush(color);
                graphics.FillEllipse(ring, offset - 1, offset - 1, diameter + 2, diameter + 2);
                graphics.FillEllipse(dot, offset, offset, diameter, diameter);
            }

            // GetHicon creates a handle this code owns: copy the icon, then release the handle.
            IntPtr handle = bitmap.GetHicon();
            try
            {
                using Icon temporary = Icon.FromHandle(handle);
                return (Icon)temporary.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _recording.PropertyChanged -= OnRecordingChanged;
                _notifyIcon.MouseClick -= OnMouseClick;
                _notifyIcon.Visible = false; // otherwise a dead icon stays until the mouse passes over it
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            _recordingIcon?.Dispose();
            _pausedIcon?.Dispose();
            _idleIcon?.Dispose();
            _recordingIcon = _pausedIcon = _idleIcon = null;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
