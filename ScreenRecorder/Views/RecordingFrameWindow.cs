using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenRecorder.Region;
using ScreenRecorder.Services;
using ScreenRecorder.Theming;

namespace ScreenRecorder.Views
{
    /// <summary>
    /// The border shown around the area (or window) being recorded. It sits just OUTSIDE the
    /// recorded rectangle, is click-through and never takes focus, and is excluded from screen
    /// capture — so it marks the area for the user without ever appearing in the recording.
    /// When recording a window it follows that window as it moves or resizes.
    /// </summary>
    public sealed class RecordingFrameWindow : Window
    {
        private const double BorderWidth = 2;
        private static readonly TimeSpan FollowInterval = TimeSpan.FromMilliseconds(50);

        private readonly IntPtr _followWindow;
        private DispatcherTimer _followTimer;
        private Rect _region;

        /// <param name="desktopRegion">Recorded rectangle in (DPI-virtualized) desktop coordinates.</param>
        /// <param name="followWindow">Window to track, or zero for a fixed area.</param>
        public RecordingFrameWindow(Rect desktopRegion, IntPtr followWindow)
        {
            _followWindow = followWindow;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SnapsToDevicePixels = true;

            var border = new Border
            {
                BorderThickness = new Thickness(BorderWidth),
                SnapsToDevicePixels = true,
            };
            border.SetResourceReference(Border.BorderBrushProperty, "RecordFillColorDefaultBrush");
            Content = border;

            SetRegion(desktopRegion);
        }

        private void SetRegion(Rect region)
        {
            if (region == _region || region.IsEmpty)
                return;

            _region = region;
            Left = region.X - BorderWidth;
            Top = region.Y - BorderWidth;
            Width = region.Width + BorderWidth * 2;
            Height = region.Height + BorderWidth * 2;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style
                | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));

            CaptureExclusionService.ExcludeAlways(this);

            if (_followWindow != IntPtr.Zero)
            {
                _followTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FollowInterval };
                _followTimer.Tick += (s, a) => Follow();
                _followTimer.Start();
            }
        }

        private void Follow()
        {
            Rect? bounds = WindowRegion.GetVirtualBounds(_followWindow);
            if (bounds == null)
            {
                // Minimized or gone: nothing to frame right now.
                if (Visibility == Visibility.Visible)
                    Visibility = Visibility.Hidden;
                return;
            }

            SetRegion(bounds.Value);
            if (Visibility != Visibility.Visible)
                Visibility = Visibility.Visible;
        }

        protected override void OnClosed(EventArgs e)
        {
            _followTimer?.Stop();
            _followTimer = null;
            base.OnClosed(e);
        }
    }
}
