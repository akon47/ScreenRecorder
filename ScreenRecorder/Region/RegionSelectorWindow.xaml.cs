using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ScreenRecorder.Region
{
    /// <summary>
    /// RegionSelectorWindow.xaml에 대한 상호 작용 논리
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
            new PropertyMetadata(RegionSelectionMode.UserRegion));

        public RegionSelectionResult RegionSelectionResult { get; private set; }

        public RegionSelectorWindow()
        {
            InitializeComponent();

            System.Drawing.Rectangle bounds = new System.Drawing.Rectangle();
            foreach (var screenBound in System.Windows.Forms.Screen.AllScreens.Select(s => s.Bounds))
            {
                bounds = System.Drawing.Rectangle.Union(bounds, screenBound);
            }


            if (bounds.Width > 0 && bounds.Height > 0)
            {
                Left = bounds.Left;
                Top = bounds.Top;
                Width = bounds.Width;
                Height = bounds.Height;

                var primaryScreenBounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                Canvas.SetLeft(regionMenuRoot, (primaryScreenBounds.Width / 2.0d) - (regionMenuRoot.Width / 2.0));
                Canvas.SetTop(regionMenuRoot, primaryScreenBounds.Top);

                using (var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height))
                {
                    using(var g = System.Drawing.Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(0, 0, 0, 0, bounds.Size, System.Drawing.CopyPixelOperation.SourceCopy);
                    }
                    ImageBrush imageBrush = new ImageBrush(Utils.ImageSourceFromBitmap(bitmap));
                    imageBrush.Freeze();
                    Background = imageBrush;
                }
                regionSelector.StartSelection();
            }
            else
            {
                throw new InvalidOperationException("width or height is zero");
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Focus();
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            regionSelector.CancelSelection();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch(e.Key)
            {
                case Key.Escape:
                    Focusable = false;
                    break;
            }
        }

        private void regionSelector_RegionSelected(object sender, RegionSelectedEventArgs e)
        {
            DialogResult = !e.IsCancelled;
            if(!e.IsCancelled)
            {
                RegionSelectionResult = new RegionSelectionResult(e.DeviceName, Rect.Offset(e.Region, -e.DisplayBounds.X, -e.DisplayBounds.Y));
            }
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            regionSelector.CancelSelection();
        }
    }
}
