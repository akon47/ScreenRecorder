using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ScreenRecorder.Region
{
    /// <summary>
    /// Selection surface for ONE monitor's overlay window (one instance per monitor, kept in
    /// sync by <see cref="RegionSelectorSession"/>). Mouse input arrives window-local and is
    /// converted to desktop coordinates against the owning monitor's bounds, so results stay in
    /// the same (possibly DPI-virtualized) desktop space the recorder maps from.
    /// </summary>
    public class RegionSelector : FrameworkElement
    {
        public RegionSelectionMode RegionSelectionMode
        {
            get { return (RegionSelectionMode)GetValue(RegionSelectionModeProperty); }
            set { SetValue(RegionSelectionModeProperty, value); }
        }

        public static readonly DependencyProperty RegionSelectionModeProperty = DependencyProperty.Register
        (
            name: "RegionSelectionMode",
            propertyType: typeof(RegionSelectionMode),
            ownerType: typeof(RegionSelector),
            typeMetadata: new FrameworkPropertyMetadata(RegionSelectionMode.WindowRegion, FrameworkPropertyMetadataOptions.AffectsRender, OnRegionSelectionModePropertyChanged)
        );

        private static void OnRegionSelectionModePropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            if (source is RegionSelector regionSelector)
            {
                regionSelector.StartSelection();
            }
        }

        public event RegionSelectedHandler RegionSelected;

        #region Private Fields
        private Point _downPoint, _movePoint;   // desktop coordinates
        private Rect _selectedTargetBounds;     // desktop coordinates
        private string _selectedTargetDevice;
        private IntPtr _selectedWindowHandle;
        private bool _selectionStarted = false;
        private WindowRegion[] _windowRegions;
        private string _deviceName;             // owning monitor
        private Rect _deviceBounds;             // owning monitor, desktop coordinates
        private double _pixelScaleX = 1.0;      // virtual → physical pixels (size label)
        private double _pixelScaleY = 1.0;
        private RegionSelectorSession _session; // shares the dragged rectangle across monitors
        #endregion

        public RegionSelector()
        {
            Focusable = false;
            _downPoint = _movePoint = new Point(0, 0);
            _selectedTargetBounds = Rect.Empty;
            _windowRegions = WindowRegion.GetWindowRegions() ?? Array.Empty<WindowRegion>();
        }

        /// <summary>Binds this selector to its session and the monitor its overlay window covers.</summary>
        internal void Attach(RegionSelectorSession session, string deviceName, Rect deviceBounds)
        {
            _session = session;
            _deviceName = deviceName;
            _deviceBounds = deviceBounds;

            var monitor = ScreenRecorder.DirectX.MonitorInfo.GetMonitorInfo(deviceName);
            if (monitor != null && monitor.Width > 0 && monitor.Height > 0)
            {
                _pixelScaleX = (double)monitor.PhysicalWidth / monitor.Width;
                _pixelScaleY = (double)monitor.PhysicalHeight / monitor.Height;
            }
        }

        private Point ToDesktop(Point localPoint)
        {
            return new Point(localPoint.X + _deviceBounds.X, localPoint.Y + _deviceBounds.Y);
        }

        private Rect ToLocal(Rect desktopRect)
        {
            if (desktopRect.IsEmpty)
                return Rect.Empty;

            return new Rect(desktopRect.X - _deviceBounds.X, desktopRect.Y - _deviceBounds.Y, desktopRect.Width, desktopRect.Height);
        }

        #region Mouse Event Handlers

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (!_selectionStarted)
                return;

            if (e.ChangedButton == MouseButton.Right)
            {
                CancelSelection();
                return;
            }

            if (e.ChangedButton != MouseButton.Left)
                return;

            _downPoint = _movePoint = ToDesktop(e.GetPosition(this));

            CaptureMouse();

            switch (RegionSelectionMode)
            {
                case RegionSelectionMode.UserRegion:
                    // The drag starts on this monitor; it may continue onto the others.
                    _selectedTargetDevice = _deviceName;
                    _selectedWindowHandle = IntPtr.Zero;
                    _session?.SetSelection(Rect.Empty);
                    break;
                case RegionSelectionMode.WindowRegion:
                case RegionSelectionMode.DisplayRegion:
                    if (!string.IsNullOrWhiteSpace(_selectedTargetDevice) && _selectedTargetBounds.Width > 0 && _selectedTargetBounds.Height > 0)
                    {
                        _selectionStarted = false;
                        ReleaseMouseCapture();
                        RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, _selectedTargetDevice, _deviceBounds, _selectedTargetBounds, false,
                            RegionSelectionMode == RegionSelectionMode.WindowRegion ? _selectedWindowHandle : IntPtr.Zero));
                    }
                    break;
            }

            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_selectionStarted)
                return;

            _movePoint = ToDesktop(e.GetPosition(this));

            switch (RegionSelectionMode)
            {
                case RegionSelectionMode.UserRegion:
                    Cursor = Cursors.Cross;
                    if (IsMouseCaptured)
                    {
                        // Every overlay redraws its share of the rectangle.
                        if (_session != null)
                            _session.SetSelection(GetUserRegion());
                        else
                            InvalidateVisual();
                    }
                    break;
                case RegionSelectionMode.WindowRegion:
                    Cursor = Cursors.Hand;

                    Rect previous = _selectedTargetBounds;
                    _selectedTargetBounds = Rect.Empty;
                    _selectedTargetDevice = null;
                    _selectedWindowHandle = IntPtr.Zero;
                    foreach (var windowRegion in _windowRegions)
                    {
                        if (windowRegion.Region.Contains(_movePoint))
                        {
                            _selectedTargetBounds = windowRegion.Region;
                            _selectedWindowHandle = windowRegion.Hwnd;
                            // The pointer is on this monitor by construction.
                            _selectedTargetDevice = _deviceName;
                            break;
                        }
                    }

                    if (previous != _selectedTargetBounds)
                        InvalidateVisual();
                    break;
                case RegionSelectionMode.DisplayRegion:
                    Cursor = Cursors.Hand;

                    // Hovering this overlay means hovering this monitor.
                    if (_selectedTargetBounds != _deviceBounds)
                    {
                        _selectedTargetBounds = _deviceBounds;
                        _selectedTargetDevice = _deviceName;
                        _selectedWindowHandle = IntPtr.Zero;
                        InvalidateVisual();
                    }
                    break;
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);

            if (!_selectionStarted || IsMouseCaptured)
                return;

            // The pointer moved off to another monitor's overlay — drop this one's highlight.
            if ((RegionSelectionMode == RegionSelectionMode.WindowRegion || RegionSelectionMode == RegionSelectionMode.DisplayRegion)
                && _selectedTargetBounds != Rect.Empty)
            {
                _selectedTargetBounds = Rect.Empty;
                _selectedTargetDevice = null;
                _selectedWindowHandle = IntPtr.Zero;
                InvalidateVisual();
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (!_selectionStarted || e.ChangedButton != MouseButton.Left)
                return;

            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();

                switch (RegionSelectionMode)
                {
                    case RegionSelectionMode.UserRegion:
                        Rect userRegion = GetUserRegion();
                        if (userRegion.IsEmpty || userRegion.Width < 2 || userRegion.Height < 2)
                        {
                            // A plain click (no drag): keep selecting instead of failing.
                            _downPoint = _movePoint = new Point(0, 0);
                            _session?.SetSelection(Rect.Empty);
                            InvalidateVisual();
                            break;
                        }

                        _selectionStarted = false;

                        // Inside one monitor → an ordinary region of that monitor. Across
                        // monitors → no device: the desktop rectangle itself is the result.
                        var monitor = _session?.FindContainingMonitor(userRegion);
                        if (monitor != null)
                            RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, monitor.DeviceName, monitor.Bounds, userRegion, false, IntPtr.Zero));
                        else if (_session != null)
                            RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, null, Rect.Empty, userRegion, false, IntPtr.Zero));
                        else
                            RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, _selectedTargetDevice, _deviceBounds, userRegion, false, IntPtr.Zero));
                        break;
                    case RegionSelectionMode.WindowRegion:

                        break;
                }
            }
        }
        #endregion

        #region OnRender

        private static readonly Brush DimBrush = CreateFrozenBrush(Color.FromArgb(128, 0, 0, 0));
        private static readonly Brush LabelBackgroundBrush = CreateFrozenBrush(Color.FromArgb(230, 32, 32, 32));
        private static readonly Typeface LabelTypeface = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        private Pen _selectorPen;
        private Pen _selectorHaloPen;

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private void EnsurePens()
        {
            if (_selectorPen != null)
                return;

            Brush accent = TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Brushes.DeepSkyBlue;
            _selectorPen = new Pen(accent, 2);
            _selectorPen.Freeze();
            _selectorHaloPen = new Pen(CreateFrozenBrush(Color.FromArgb(120, 0, 0, 0)), 4);
            _selectorHaloPen.Freeze();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (!_selectionStarted)
                return;

            EnsurePens();

            double width = ActualWidth;
            double height = ActualHeight;
            var bounds = new Rect(0, 0, width, height);

            // Hit-testable everywhere.
            dc.DrawRectangle(Brushes.Transparent, null, bounds);

            // "highlight" is the part of the selection on this monitor; "whole" is the complete
            // selection in this window's coordinates, which reaches past the window when the
            // selection continues on the next monitor.
            Rect highlight = Rect.Empty;
            Rect whole = Rect.Empty;
            switch (RegionSelectionMode)
            {
                case RegionSelectionMode.UserRegion:
                    Rect selection = _session != null ? _session.Selection : GetUserRegion();
                    if (!selection.IsEmpty)
                    {
                        highlight = ToLocal(Rect.Intersect(_deviceBounds, selection));
                        whole = ToLocal(selection);
                    }
                    break;
                case RegionSelectionMode.WindowRegion:
                    if (!_selectedTargetBounds.IsEmpty)
                    {
                        highlight = ToLocal(Rect.Intersect(_deviceBounds, _selectedTargetBounds));
                        whole = ToLocal(_selectedTargetBounds);
                    }
                    break;
                case RegionSelectionMode.DisplayRegion:
                    highlight = ToLocal(_selectedTargetBounds);
                    whole = highlight;
                    break;
            }

            if (highlight.IsEmpty || highlight.Width <= 0 || highlight.Height <= 0)
            {
                dc.DrawRectangle(DimBrush, null, bounds);
                return;
            }

            // Dim everything except the highlight: four bands around it.
            highlight.Intersect(bounds);
            dc.DrawRectangle(DimBrush, null, new Rect(0, 0, width, Math.Max(0, highlight.Top)));
            dc.DrawRectangle(DimBrush, null, new Rect(0, highlight.Bottom, width, Math.Max(0, height - highlight.Bottom)));
            dc.DrawRectangle(DimBrush, null, new Rect(0, highlight.Top, Math.Max(0, highlight.Left), highlight.Height));
            dc.DrawRectangle(DimBrush, null, new Rect(highlight.Right, highlight.Top, Math.Max(0, width - highlight.Right), highlight.Height));

            // Accent outline (with a soft dark halo so it reads on any content), inside the area.
            // It is the outline of the WHOLE selection, clipped to this monitor's part: where
            // the selection continues on the neighboring monitor there is no edge to draw, so
            // no line appears along the seam between the monitors.
            if (whole.IsEmpty)
                whole = highlight;
            var outline = new Rect(whole.X + 1, whole.Y + 1, Math.Max(0, whole.Width - 2), Math.Max(0, whole.Height - 2));
            dc.PushClip(new RectangleGeometry(highlight));
            dc.DrawRectangle(null, _selectorHaloPen, outline);
            dc.DrawRectangle(null, _selectorPen, outline);
            dc.Pop();

            if (ShouldDrawSizeLabel())
                DrawSizeLabel(dc, highlight, bounds);
        }

        /// <summary>
        /// A rectangle spanning monitors is drawn by several overlays; only the one holding its
        /// top-left corner labels it.
        /// </summary>
        private bool ShouldDrawSizeLabel()
        {
            if (RegionSelectionMode != RegionSelectionMode.UserRegion || _session == null || _session.Selection.IsEmpty)
                return true;

            return _deviceBounds.Contains(_session.Selection.TopLeft);
        }

        /// <summary>Draws "1280 × 720" (real pixels) just above — or, when there is no room, inside — the selection.</summary>
        private void DrawSizeLabel(DrawingContext dc, Rect highlight, Rect bounds)
        {
            int pixelWidth = (int)Math.Round(highlight.Width * _pixelScaleX) & ~1;
            int pixelHeight = (int)Math.Round(highlight.Height * _pixelScaleY) & ~1;

            // A dragged area reports the size of the whole selection, not just this monitor's share.
            if (RegionSelectionMode == RegionSelectionMode.UserRegion && _session != null && !_session.Selection.IsEmpty)
            {
                pixelWidth = (int)_session.SelectionPixelSize.Width;
                pixelHeight = (int)_session.SelectionPixelSize.Height;
            }

            if (pixelWidth <= 0 || pixelHeight <= 0)
                return;

            string text = string.Format(CultureInfo.InvariantCulture, "{0} × {1}", pixelWidth, pixelHeight);
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                LabelTypeface, 12, Brushes.White, 1.0);

            const double padX = 8, padY = 4, gap = 6;
            double labelWidth = formatted.Width + padX * 2;
            double labelHeight = formatted.Height + padY * 2;

            double x = Math.Max(bounds.Left + 4, Math.Min(highlight.Left, bounds.Right - labelWidth - 4));
            double y = highlight.Top - labelHeight - gap;
            if (y < bounds.Top + 4)
                y = Math.Min(highlight.Top + gap, bounds.Bottom - labelHeight - 4);

            dc.DrawRoundedRectangle(LabelBackgroundBrush, null, new Rect(x, y, labelWidth, labelHeight), 4, 4);
            dc.DrawText(formatted, new Point(x + padX, y + padY));
        }

        #endregion

        #region Private Methods

        private Rect GetUserRegion()
        {
            var selectorRegion = new Rect((int)Math.Min(_downPoint.X, _movePoint.X), (int)Math.Min(_downPoint.Y, _movePoint.Y), (int)Math.Abs(_downPoint.X - _movePoint.X), (int)Math.Abs(_downPoint.Y - _movePoint.Y));

            // Clamped to the whole desktop (all monitors), not to the monitor the drag started on.
            Rect limit = _session != null && !_session.DesktopBounds.IsEmpty ? _session.DesktopBounds : _deviceBounds;
            return Rect.Intersect(limit, selectorRegion);
        }

        #endregion

        #region Public Methods
        public void StartSelection()
        {
            _selectionStarted = true;
            _downPoint = _movePoint = new Point(0, 0);
            _selectedTargetBounds = Rect.Empty;
            _selectedTargetDevice = null;
            _selectedWindowHandle = IntPtr.Zero;
            InvalidateVisual();
        }

        public void CancelSelection()
        {
            if (_selectionStarted)
            {
                _selectionStarted = false;
                if (IsMouseCaptured)
                    ReleaseMouseCapture();
                RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, null, Rect.Empty, Rect.Empty, true, IntPtr.Zero));
                InvalidateVisual();
            }
        }
        #endregion
    }
}
