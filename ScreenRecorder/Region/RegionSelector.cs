using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ScreenRecorder.Region
{
    public class RegionSelector : FrameworkElement
    {
        public RegionSelectionMode RegionSelectionMode
        {
            get { return (RegionSelectionMode)GetValue(RegionSelectionModeProperty); }
            set { SetValue(RegionSelectionModeProperty, value); }
        }
        public static readonly DependencyProperty RegionSelectionModeProperty =
            DependencyProperty.Register("RegionSelectionMode", typeof(RegionSelectionMode), typeof(RegionSelector),
            new FrameworkPropertyMetadata(RegionSelectionMode.WindowRegion, FrameworkPropertyMetadataOptions.AffectsRender,
                new PropertyChangedCallback(OnRegionSelectionModePropertyChanged)));
        private static void OnRegionSelectionModePropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            if (source is RegionSelector regionSelector)
            {
                regionSelector.StartSelection();
            }
        }

        public event RegionSelectedHandler RegionSelected;

        #region Private Fields
        private Point _downPoint, _movePoint;
        private Rect _selectedTargetBounds;
        private string _selectedTargetDevice;
        private bool _selectionStarted = false;
        private WindowRegion[] _windowRegions;
        private System.Windows.Forms.Screen[] _screens;
        #endregion

        public RegionSelector()
        {
            Focusable = false;
            _downPoint = _movePoint = new Point(0, 0);
            _selectedTargetBounds = Rect.Empty;
            _screens = System.Windows.Forms.Screen.AllScreens;
            _windowRegions = WindowRegion.GetWindowRegions();
        }



        #region Mouse Event Handlers

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (!_selectionStarted)
                return;

            _downPoint = _movePoint = e.GetPosition(this);

            CaptureMouse();

            switch (RegionSelectionMode)
            {
                case RegionSelectionMode.UserRegion:
                    var screen = _screens.FirstOrDefault(s => s.Bounds.Contains((int)_downPoint.X, (int)_downPoint.Y));
                    if (screen != null)
                    {
                        _selectedTargetDevice = screen.DeviceName;
                    }
                    break;
                case RegionSelectionMode.WindowRegion:
                case RegionSelectionMode.DisplayRegion:
                    if (!string.IsNullOrWhiteSpace(_selectedTargetDevice) && _selectedTargetBounds.Width > 0 && _selectedTargetBounds.Height > 0)
                    {
                        _selectionStarted = false;
                        RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, _selectedTargetDevice, GetScreenBounds(_selectedTargetDevice), _selectedTargetBounds, !(_selectedTargetBounds.Width > 0 && _selectedTargetBounds.Height > 0)));
                    }
                    break;
            }

            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_selectionStarted)
                return;

            _movePoint = e.GetPosition(this);

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
                            _selectedTargetDevice = _screens.FirstOrDefault(s => s.Bounds.Contains((int)_movePoint.X, (int)_movePoint.Y))?.DeviceName;
                            break;
                        }
                    }
                    InvalidateVisual();
                    break;
                case RegionSelectionMode.DisplayRegion:
                    Cursor = Cursors.Arrow;

                    var screen = _screens.FirstOrDefault(s => s.Bounds.Contains((int)_movePoint.X, (int)_movePoint.Y));
                    if (screen != null)
                    {
                        Rect bounds = new Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
                        if (_selectedTargetBounds != bounds)
                        {
                            _selectedTargetBounds = bounds;
                            _selectedTargetDevice = screen.DeviceName;
                            InvalidateVisual();
                        }
                    }
                    break;
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
                        RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, _selectedTargetDevice, GetScreenBounds(_selectedTargetDevice), userRegion, !(userRegion.Width > 0 && userRegion.Height > 0)));
                        break;
                    case RegionSelectionMode.WindowRegion:

                        break;
                }
            }
        }
        #endregion

        #region OnRender
        private Pen _selectorPen = new Pen(Brushes.White, 2);
        private Brush _dimBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        protected override void OnRender(DrawingContext dc)
        {
            if (!_selectionStarted)
                return;

            Rect bounds = new Rect(0, 0, ActualWidth, ActualHeight);

            dc.DrawRectangle(Brushes.Transparent, null, bounds);

            PathGeometry pathGeometry = new PathGeometry();
            pathGeometry.AddGeometry(new RectangleGeometry(bounds));
            switch (RegionSelectionMode)
            {
                case RegionSelectionMode.UserRegion:
                    Rect userRegion = GetUserRegion();
                    pathGeometry.AddGeometry(new RectangleGeometry(userRegion));
                    dc.DrawRectangle(null, _selectorPen, userRegion);
                    break;
                case RegionSelectionMode.WindowRegion:
                    Rect windowRegion = Rect.Intersect(GetDeviceRegion(_selectedTargetDevice), _selectedTargetBounds);
                    pathGeometry.AddGeometry(new RectangleGeometry(windowRegion));
                    dc.DrawRectangle(null, _selectorPen, windowRegion);
                    break;
                case RegionSelectionMode.DisplayRegion:
                    pathGeometry.AddGeometry(new RectangleGeometry(_selectedTargetBounds));
                    dc.DrawRectangle(null, _selectorPen, _selectedTargetBounds);
                    break;
            }
            dc.DrawGeometry(_dimBrush, null, pathGeometry);
        }
        #endregion

        #region Private Methods

        private Rect GetScreenBounds(string deviceName)
        {
            var screen = _screens.FirstOrDefault(s => s.DeviceName == deviceName);
            if(screen != null)
            {
                return new Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
            }
            else
            {
                return Rect.Empty;
            }
        }

        private Rect GetUserRegion()
        {
            Rect selectorRegion = new Rect((int)Math.Min(_downPoint.X, _movePoint.X), (int)Math.Min(_downPoint.Y, _movePoint.Y), (int)Math.Abs(_downPoint.X - _movePoint.X), (int)Math.Abs(_downPoint.Y - _movePoint.Y));
            return Rect.Intersect(GetDeviceRegion(_selectedTargetDevice), selectorRegion);
        }

        private Rect GetDeviceRegion(string deviceName)
        {
            var screen = _screens.FirstOrDefault(s => s.DeviceName == deviceName);
            if (screen != null)
            {
                return new Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
            }
            else
            {
                return Rect.Empty;
            }
        }
        #endregion

        #region Public Methods
        public void StartSelection()
        {
            _selectionStarted = true;
            _downPoint = _movePoint = new Point(0, 0);
            _selectedTargetBounds = Rect.Empty;
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
