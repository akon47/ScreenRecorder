using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ScreenRecorder.Theming;

namespace ScreenRecorder.Views
{
    /// <summary>
    /// The click highlight: a ring that expands and fades at the click position. One small
    /// click-through, never-focused overlay window, reused for every click. Unlike the app's
    /// other windows it is NOT hidden from capture — being recorded is its whole purpose.
    /// </summary>
    public sealed class ClickRingWindow : Window
    {
        private const double Size = 64;
        private static readonly Duration AnimationDuration = new Duration(TimeSpan.FromMilliseconds(380));

        private static readonly Brush LeftBrush = Frozen(Color.FromRgb(0xFF, 0xC8, 0x00));   // amber
        private static readonly Brush RightBrush = Frozen(Color.FromRgb(0x4C, 0xC2, 0xFF));  // blue

        private readonly Ellipse _ring;
        private readonly ScaleTransform _scale = new ScaleTransform(0.3, 0.3, Size / 2, Size / 2);

        public ClickRingWindow()
        {
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
            Width = Size;
            Height = Size;

            _ring = new Ellipse
            {
                Width = Size - 8,
                Height = Size - 8,
                StrokeThickness = 4,
                Opacity = 0,
                RenderTransform = _scale,
                RenderTransformOrigin = new Point(0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Content = _ring;
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style
                | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));
        }

        /// <summary>Plays the ring centered on <paramref name="center"/> (desktop coordinates).</summary>
        public void Play(Point center, bool rightButton)
        {
            Left = center.X - Size / 2;
            Top = center.Y - Size / 2;
            _ring.Stroke = rightButton ? RightBrush : LeftBrush;

            if (!IsVisible)
                Show();

            var grow = new DoubleAnimation(0.3, 1.0, AnimationDuration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var fade = new DoubleAnimation(0.95, 0.0, AnimationDuration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };

            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            _ring.BeginAnimation(OpacityProperty, fade);
        }
    }
}
