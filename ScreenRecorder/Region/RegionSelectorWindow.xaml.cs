using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ScreenRecorder.Region
{
    /// <summary>
    /// One selection overlay covering exactly ONE monitor, created and coordinated by
    /// <see cref="RegionSelectorSession"/>. Per-monitor windows are required for mixed-DPI
    /// setups: DWM scales a DPI-unaware window by a single monitor's factor, so one
    /// desktop-spanning window cannot cover a 100%+200% layout.
    /// </summary>
    public partial class RegionSelectorWindow : Window
    {
        public RegionSelectionMode RegionSelectionMode
        {
            get { return (RegionSelectionMode)GetValue(RegionSelectionModeProperty); }
            set { SetValue(RegionSelectionModeProperty, value); }
        }
        public static readonly DependencyProperty RegionSelectionModeProperty =
            DependencyProperty.Register("RegionSelectionMode", typeof(RegionSelectionMode), typeof(RegionSelectorWindow),
            new PropertyMetadata(RegionSelectionMode.UserRegion, OnRegionSelectionModePropertyChanged));

        private static void OnRegionSelectionModePropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            if (source is RegionSelectorWindow window)
                window._session?.OnModeChanged((RegionSelectionMode)e.NewValue);
        }

        private readonly RegionSelectorSession _session;

        /// <summary>The mode-switch menu is shown on the primary monitor's window only.</summary>
        internal bool ShowsMenu { get; }

        internal RegionSelectorWindow(RegionSelectorSession session, System.Windows.Forms.Screen screen, bool showMenu)
        {
            _session = session;
            ShowsMenu = showMenu;

            InitializeComponent();

            RegionSelectionMode = session.RegionSelectionMode;

            var bounds = screen.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new InvalidOperationException("width or height is zero");
            }

            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;

            if (!showMenu)
            {
                regionMenuRoot.Visibility = Visibility.Collapsed;
            }

            // GDI screen reads are physical pixels (never DPI-virtualized): capture the monitor's
            // PHYSICAL rect and scale it to the window's (virtualized) size. Capturing the virtual
            // rect on a scaled monitor grabs only its top-left portion (200% → top-left quarter).
            var monitorInfo = ScreenRecorder.DirectX.MonitorInfo.GetMonitorInfo(screen.DeviceName);
            int physicalLeft = monitorInfo?.PhysicalLeft ?? bounds.Left;
            int physicalTop = monitorInfo?.PhysicalTop ?? bounds.Top;
            int physicalWidth = monitorInfo?.PhysicalWidth ?? bounds.Width;
            int physicalHeight = monitorInfo?.PhysicalHeight ?? bounds.Height;

            using (var physicalBitmap = new System.Drawing.Bitmap(physicalWidth, physicalHeight))
            {
                using (var g = System.Drawing.Graphics.FromImage(physicalBitmap))
                {
                    g.CopyFromScreen(physicalLeft, physicalTop, 0, 0, new System.Drawing.Size(physicalWidth, physicalHeight), System.Drawing.CopyPixelOperation.SourceCopy);
                }

                var displayBitmap = physicalBitmap;
                if (physicalWidth != bounds.Width || physicalHeight != bounds.Height)
                {
                    displayBitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
                    using (var g = System.Drawing.Graphics.FromImage(displayBitmap))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(physicalBitmap, new System.Drawing.Rectangle(0, 0, bounds.Width, bounds.Height));
                    }
                }

                try
                {
                    ImageBrush imageBrush = new ImageBrush(ToImageSource(displayBitmap));
                    imageBrush.Freeze();
                    Background = imageBrush;
                }
                finally
                {
                    if (!ReferenceEquals(displayBitmap, physicalBitmap))
                    {
                        displayBitmap.Dispose();
                    }
                }
            }

            regionSelector.Attach(session, screen.DeviceName, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            regionSelector.StartSelection();
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        private static ImageSource ToImageSource(System.Drawing.Bitmap bitmap)
        {
            IntPtr handle = bitmap.GetHbitmap();
            try
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    handle, IntPtr.Zero, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(handle);
            }
        }

        /// <summary>Redraws the selection (the shared drag rectangle changed).</summary>
        internal void InvalidateSelection() => regionSelector.InvalidateVisual();

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (ShowsMenu)
            {
                Focus();
            }
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);

            // Focus hopping between the session's overlays (clicking another monitor) must not
            // cancel; focus leaving the session (alt-tab, another app window) must.
            if (!_session.ContainsFocus(e.NewFocus))
            {
                _session.Cancel();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.Key)
            {
                case Key.Escape:
                    _session.Cancel();
                    break;
            }
        }

        private void regionSelector_RegionSelected(object sender, RegionSelectedEventArgs e)
        {
            if (e.IsCancelled)
            {
                _session.Cancel();
            }
            else
            {
                // No device name = the area spans monitors: only its desktop rectangle is meaningful.
                Rect monitorRelative = e.DeviceName == null
                    ? Rect.Empty
                    : Rect.Offset(e.Region, -e.DisplayBounds.X, -e.DisplayBounds.Y);

                _session.Complete(new RegionSelectionResult(e.DeviceName, monitorRelative, e.Region, e.WindowHandle, e.RegionSelectionMode));
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _session.Cancel();
        }
    }
}
