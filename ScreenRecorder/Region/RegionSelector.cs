using System;
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
        private bool _selectionStarted = false;
        private WindowRegion[] _windowRegions;
        private string _deviceName;             // owning monitor
        private Rect _deviceBounds;             // owning monitor, desktop coordinates
        #endregion

        public RegionSelector()
        {
            Focusable = false;
            _downPoint = _movePoint = new Point(0, 0);
            _selectedTargetBounds = Rect.Empty;
            _windowRegions = WindowRegion.GetWindowRegions() ?? Array.Empty<WindowRegion>();
        }

        /// <summary>Binds this selector to the monitor its overlay window covers.</summary>
        public void Attach(string deviceName, Rect deviceBounds)
        {
            _deviceName = deviceName;
            _deviceBounds = deviceBounds;
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

            _downPoint = _movePoint = ToDesktop(e.GetPosition(this));

            CaptureMouse();

            switch (RegionSelectionMode)
            {
                case RegionSelectionMode.UserRegion:
                    // The drag starts on this monitor and is clamped to it.
                    _selectedTargetDevice = _deviceName;
                    break;
                case RegionSelectionMode.WindowRegion:
                case RegionSelectionMode.DisplayRegion:
                    if (!string.IsNullOrWhiteSpace(_selectedTargetDevice) && _selectedTargetBounds.Width > 0 && _selectedTargetBounds.Height > 0)
                    {
                        _selectionStarted = false;
                        RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, _selectedTargetDevice, _deviceBounds, _selectedTargetBounds, !(_selectedTargetBounds.Width > 0 && _selectedTargetBounds.Height > 0)));
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
                        InvalidateVisual();
                    }
                    break;
                case RegionSelectionMode.WindowRegion:
                    Cursor = Cursors.Arrow;

                    _selectedTargetBounds = Rect.Empty;
                    _selectedTargetDevice = null;
                    foreach (var windowRegion in _windowRegions)
                    {
                        if (windowRegion.Region.Contains(_movePoint))
                        {
                            _selectedTargetBounds = windowRegion.Region;
                            // The pointer is on this monitor by construction.
                            _selectedTargetDevice = _deviceName;
                            break;
                        }
                    }
                    InvalidateVisual();
                    break;
                case RegionSelectionMode.DisplayRegion:
                    Cursor = Cursors.Arrow;

                    // Hovering this overlay means hovering this monitor.
                    if (_selectedTargetBounds != _deviceBounds)
                    {
                        _selectedTargetBounds = _deviceBounds;
                        _selectedTargetDevice = _deviceName;
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
                InvalidateVisual();
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (!_selectionStarted)
                return;

            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();

                switch (RegionSelectionMode)
                {
                    case RegionSelectionMode.UserRegion:
                        _selectionStarted = false;
                        Rect userRegion = GetUserRegion();
                        RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, _selectedTargetDevice, _deviceBounds, userRegion, !(userRegion.Width > 0 && userRegion.Height > 0)));
                        break;
                    case RegionSelectionMode.WindowRegion:

                        break;
                }
            }
        }
        #endregion

        #region OnRender

        private readonly Pen _selectorPen = new Pen(Brushes.White, 2);
        private readonly Brush _dimBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));

        protected override void OnRender(DrawingContext dc)
        {
            if (!_selectionStarted)
                return;

            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);

            dc.DrawRectangle(Brushes.Transparent, null, bounds);

            var pathGeometry = new PathGeometry();
            pathGeometry.AddGeometry(new RectangleGeometry(bounds));

            Rect highlight = Rect.Empty;
            switch (RegionSelectionMode)
            {
                case RegionSelectionMode.UserRegion:
                    highlight = ToLocal(GetUserRegion());
                    break;
                case RegionSelectionMode.WindowRegion:
                    highlight = _selectedTargetBounds.IsEmpty ? Rect.Empty : ToLocal(Rect.Intersect(_deviceBounds, _selectedTargetBounds));
                    break;
                case RegionSelectionMode.DisplayRegion:
                    highlight = ToLocal(_selectedTargetBounds);
                    break;
            }

            if (!highlight.IsEmpty)
            {
                pathGeometry.AddGeometry(new RectangleGeometry(highlight));
                dc.DrawRectangle(null, _selectorPen, highlight);
            }

            dc.DrawGeometry(_dimBrush, null, pathGeometry);
        }

        #endregion

        #region Private Methods

        private Rect GetUserRegion()
        {
            var selectorRegion = new Rect((int)Math.Min(_downPoint.X, _movePoint.X), (int)Math.Min(_downPoint.Y, _movePoint.Y), (int)Math.Abs(_downPoint.X - _movePoint.X), (int)Math.Abs(_downPoint.Y - _movePoint.Y));
            return Rect.Intersect(_deviceBounds, selectorRegion);
        }

        #endregion

        #region Public Methods
        public void StartSelection()
        {
            _selectionStarted = true;
            _downPoint = _movePoint = new Point(0, 0);
            _selectedTargetBounds = Rect.Empty;
            _selectedTargetDevice = null;
            InvalidateVisual();
        }

        public void CancelSelection()
        {
            if (_selectionStarted)
            {
                _selectionStarted = false;
                RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, null, Rect.Empty, Rect.Empty, true));
                InvalidateVisual();
            }
        }
        #endregion
    }
}
