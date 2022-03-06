using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
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
        private Point downPoint, movePoint;   // relative to 0,0 (left,top)
        private Rect selectedTargetBounds;
        private IntPtr selectedHwnd;
        private string selectedTargetDevice;
        private bool selectionStarted = false;
        private WindowRegion[] windowRegions;
        private System.Windows.Forms.Screen[] screens;
        private double _minLeft;
        private double _minTop;
        #endregion

        public RegionSelector()
        {
            Focusable = false;
            downPoint = movePoint = new Point(0, 0);
            selectedTargetBounds = Rect.Empty;
            screens = System.Windows.Forms.Screen.AllScreens;
            windowRegions = WindowRegion.GetWindowRegions();
            _minLeft = screens.Min(x => x.Bounds.Left);
            _minTop = screens.Min(x => x.Bounds.Top);
            selectedHwnd = IntPtr.Zero;
        }

        #region Mouse Event Handlers

        private System.Windows.Forms.Screen GetScreenDeviceFromMousePoint(Point mousePoint)
        {
            mousePoint.Offset(_minLeft, _minTop);
            return screens.FirstOrDefault(s => s.Bounds.Contains((int)mousePoint.X, (int)mousePoint.Y));
        }

        private WindowRegion GetWindowRegionFromMousePoint(Point mousePoint)
        {
            mousePoint.Offset(_minLeft, _minTop);
            return windowRegions.FirstOrDefault(s => s.Region.Contains((int)mousePoint.X, (int)mousePoint.Y));
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (!selectionStarted)
                return;

            downPoint = movePoint = e.GetPosition(this);

            CaptureMouse();

            switch (RegionSelectionMode)
            {
                case RegionSelectionMode.UserRegion:
                    var screen = GetScreenDeviceFromMousePoint(movePoint);
                    selectedTargetDevice = screen?.DeviceName;
                    break;
                case RegionSelectionMode.WindowRegion:
                case RegionSelectionMode.DisplayRegion:
                    // values should be set before in OnMouseMove
                    if (!string.IsNullOrWhiteSpace(selectedTargetDevice) && selectedTargetBounds.Width > 0 && selectedTargetBounds.Height > 0)
                    {
                        selectionStarted = false;
                        RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, selectedTargetDevice, GetDeviceRegion(selectedTargetDevice), 
                            selectedTargetBounds, selectedHwnd, !(selectedTargetBounds.Width > 0 && selectedTargetBounds.Height > 0)));
                    }
                    break;
            }

            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!selectionStarted)
                return;

            movePoint = e.GetPosition(this);

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

                    selectedTargetBounds = Rect.Empty;
                    selectedTargetDevice = null;
                    selectedHwnd = IntPtr.Zero;
                    var windowRegion = GetWindowRegionFromMousePoint(movePoint);
                    if (windowRegion != null)
                    {
                        selectedTargetBounds = windowRegion.Region;
                        selectedTargetDevice = GetScreenDeviceFromMousePoint(movePoint)?.DeviceName;
                        selectedHwnd = windowRegion.Hwnd;
                    }
                    InvalidateVisual();
                    break;
                case RegionSelectionMode.DisplayRegion:
                    Cursor = Cursors.Arrow;

                    selectedTargetDevice = null;

                    var screen = GetScreenDeviceFromMousePoint(movePoint);
                    if (screen != null)
                    {
                        Rect bounds = new Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
                        selectedTargetBounds = bounds;
                        selectedTargetDevice = screen.DeviceName;
                        InvalidateVisual();
                    }
                    break;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (!selectionStarted)
                return;

            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();

                switch (RegionSelectionMode)
                {
                    case RegionSelectionMode.UserRegion:
                        selectionStarted = false;
                        Rect userRegion = GetUserRegion(downPoint, movePoint, true);
                        RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, selectedTargetDevice, GetDeviceRegion(selectedTargetDevice), 
                            userRegion, selectedHwnd, !(userRegion.Width > 0 && userRegion.Height > 0)));
                        break;
                    case RegionSelectionMode.WindowRegion:
                        break;
                }
            }
        }
        #endregion

        #region OnRender
        private Pen selectorPen = new Pen(Brushes.White, 2);
        private Brush dimBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        protected override void OnRender(DrawingContext dc)
        {
            if (!selectionStarted)
                return;

            Rect bounds = new Rect(0, 0, ActualWidth, ActualHeight);

            dc.DrawRectangle(Brushes.Transparent, null, bounds);

            PathGeometry pathGeometry = new PathGeometry();
            pathGeometry.AddGeometry(new RectangleGeometry(bounds));
            switch (RegionSelectionMode)
            {
                case RegionSelectionMode.UserRegion:
                    Rect userRegion = GetUserRegion(downPoint, movePoint, false);
                    pathGeometry.AddGeometry(new RectangleGeometry(userRegion));
                    dc.DrawRectangle(null, selectorPen, userRegion);
                    break;
                case RegionSelectionMode.WindowRegion:
                case RegionSelectionMode.DisplayRegion:
                    if (!selectedTargetBounds.IsEmpty)
                    {
                        Rect windowRegion = Rect.Intersect(GetDeviceRegion(selectedTargetDevice), selectedTargetBounds);
                        windowRegion.Offset(-_minLeft, -_minTop);
                        pathGeometry.AddGeometry(new RectangleGeometry(windowRegion));
                        dc.DrawRectangle(null, selectorPen, windowRegion);
                    }
                    break;
            }
            dc.DrawGeometry(dimBrush, null, pathGeometry);
        }
        #endregion

        #region Private Methods

        private Rect GetUserRegion(Point point1, Point point2, bool absCoord)
        {
            Rect selectorRegion = new Rect((int)Math.Min(point1.X, point2.X), (int)Math.Min(point1.Y, point2.Y), (int)Math.Abs(point1.X - point2.X), (int)Math.Abs(point1.Y - point2.Y));
            Rect deviceRegion = GetDeviceRegion(selectedTargetDevice);
            if (deviceRegion.IsEmpty)
                return deviceRegion;
            if (absCoord)
                selectorRegion.Offset(_minLeft, _minTop);   // links oben absolute Monitorgrenzen
            else
                deviceRegion.Offset(-_minLeft, -_minTop);   // links oben ist 0,0

            return Rect.Intersect(deviceRegion, selectorRegion);
        }

        private Rect GetDeviceRegion(string deviceName)
        {
            var screen = screens.FirstOrDefault(s => s.DeviceName == deviceName);
            return (screen != null)
                ? new Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height)
                : Rect.Empty;
        }
        #endregion

        #region Public Methods
        public void StartSelection()
        {
            selectionStarted = true;
            downPoint = movePoint = new Point(0, 0);
            selectedTargetBounds = Rect.Empty;
            selectedHwnd = IntPtr.Zero;
            InvalidateVisual();
        }

        public void CancelSelection()
        {
            if (selectionStarted)
            {
                selectionStarted = false;
                RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, null, Rect.Empty, Rect.Empty, IntPtr.Zero, true));
                InvalidateVisual();
            }
        }
        #endregion
    }
}
