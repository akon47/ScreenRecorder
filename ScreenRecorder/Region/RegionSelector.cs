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
        private Point downPoint, movePoint;
        private Rect selectedTargetBounds;
        private string selectedTargetDevice;
        private bool selectionStarted = false;
        private WindowRegion[] windowRegions;
        private System.Windows.Forms.Screen[] screens;
        #endregion

        public RegionSelector()
        {
            Focusable = false;
            downPoint = movePoint = new Point(0, 0);
            selectedTargetBounds = Rect.Empty;
            screens = System.Windows.Forms.Screen.AllScreens;
            windowRegions = WindowRegion.GetWindowRegions();
        }



        #region Mouse Event Handlers

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (!selectionStarted)
                return;

            downPoint = movePoint = e.GetPosition(this);

            CaptureMouse();

            switch (RegionSelectionMode)
            {
                case RegionSelectionMode.UserRegion:
                    var screen = screens.FirstOrDefault(s => s.Bounds.Contains((int)downPoint.X, (int)downPoint.Y));
                    if (screen != null)
                    {
                        selectedTargetDevice = screen.DeviceName;
                    }
                    break;
                case RegionSelectionMode.WindowRegion:
                case RegionSelectionMode.DisplayRegion:
                    if (!string.IsNullOrWhiteSpace(selectedTargetDevice) && selectedTargetBounds.Width > 0 && selectedTargetBounds.Height > 0)
                    {
                        selectionStarted = false;
                        RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, selectedTargetDevice, GetScreenBounds(selectedTargetDevice), selectedTargetBounds, !(selectedTargetBounds.Width > 0 && selectedTargetBounds.Height > 0)));
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
                    foreach (var windowRegion in windowRegions)
                    {
                        if (windowRegion.Region.Contains(movePoint))
                        {
                            selectedTargetBounds = windowRegion.Region;
                            selectedTargetDevice = screens.FirstOrDefault(s => s.Bounds.Contains((int)movePoint.X, (int)movePoint.Y))?.DeviceName;
                            break;
                        }
                    }
                    InvalidateVisual();
                    break;
                case RegionSelectionMode.DisplayRegion:
                    Cursor = Cursors.Arrow;

                    var screen = screens.FirstOrDefault(s => s.Bounds.Contains((int)movePoint.X, (int)movePoint.Y));
                    if (screen != null)
                    {
                        Rect bounds = new Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
                        if (selectedTargetBounds != bounds)
                        {
                            selectedTargetBounds = bounds;
                            selectedTargetDevice = screen.DeviceName;
                            InvalidateVisual();
                        }
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
                        Rect userRegion = GetUserRegion();
                        RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, selectedTargetDevice, GetScreenBounds(selectedTargetDevice), userRegion, !(userRegion.Width > 0 && userRegion.Height > 0)));
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
                    Rect userRegion = GetUserRegion();
                    pathGeometry.AddGeometry(new RectangleGeometry(userRegion));
                    dc.DrawRectangle(null, selectorPen, userRegion);
                    break;
                case RegionSelectionMode.WindowRegion:
                    Rect windowRegion = Rect.Intersect(GetDeviceRegion(selectedTargetDevice), selectedTargetBounds);
                    pathGeometry.AddGeometry(new RectangleGeometry(windowRegion));
                    dc.DrawRectangle(null, selectorPen, windowRegion);
                    break;
                case RegionSelectionMode.DisplayRegion:
                    pathGeometry.AddGeometry(new RectangleGeometry(selectedTargetBounds));
                    dc.DrawRectangle(null, selectorPen, selectedTargetBounds);
                    break;
            }
            dc.DrawGeometry(dimBrush, null, pathGeometry);
        }
        #endregion

        #region Private Methods

        private Rect GetScreenBounds(string deviceName)
        {
            var screen = screens.FirstOrDefault(s => s.DeviceName == deviceName);
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
            Rect selectorRegion = new Rect((int)Math.Min(downPoint.X, movePoint.X), (int)Math.Min(downPoint.Y, movePoint.Y), (int)Math.Abs(downPoint.X - movePoint.X), (int)Math.Abs(downPoint.Y - movePoint.Y));
            return Rect.Intersect(GetDeviceRegion(selectedTargetDevice), selectorRegion);
        }

        private Rect GetDeviceRegion(string deviceName)
        {
            var screen = screens.FirstOrDefault(s => s.DeviceName == deviceName);
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
            selectionStarted = true;
            downPoint = movePoint = new Point(0, 0);
            selectedTargetBounds = Rect.Empty;
            InvalidateVisual();
        }

        public void CancelSelection()
        {
            if (selectionStarted)
            {
                selectionStarted = false;
                RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, null, Rect.Empty, Rect.Empty, true));
                InvalidateVisual();
            }
        }
        #endregion
    }
}
