using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using ScreenRecorder.Extensions;

namespace ScreenRecorder.Behaviors
{
    public static class PopupBehavior
    {
        public static readonly DependencyProperty DisplayedOnlyMonitorProperty =
            DependencyProperty.RegisterAttached(
                "DisplayedOnlyMonitor", typeof(bool), typeof(PopupBehavior), new PropertyMetadata(false, DisplayedOnlyMonitorPropertyChanged));
        private static void DisplayedOnlyMonitorPropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            Apply(source as Popup);
        }

        private static void Apply(Popup popup)
        {
            if (popup == null)
                throw new ArgumentNullException(nameof(popup));

            popup.Opened -= Popup_Opened;
            if (GetDisplayedOnlyMonitor(popup))
            {
                popup.Opened += Popup_Opened;
            }
        }

        private static void Popup_Opened(object sender, EventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.Popup popup)
            {
                if (PopupBehavior.GetDisplayedOnlyMonitor(popup))
                {
                    IntPtr popupWindowHandle = popup.GetPopupWindowHandle();
                    if (popupWindowHandle != IntPtr.Zero)
                    {
                        var excludeFromCapture = AppManager.Instance.ScreenEncoder.IsStarted && AppConfig.Instance.ExcludeFromCapture;

                        var isWindowDisplayedOnlyMonitor = Utils.IsWindowDisplayedOnlyMonitor(popupWindowHandle);
                        if (excludeFromCapture != isWindowDisplayedOnlyMonitor)
                        {
                            Utils.SetWindowDisplayedOnlyMonitor(popupWindowHandle, excludeFromCapture);
                        }
                    }
                }
            }
        }

        public static void SetDisplayedOnlyMonitor(Popup popup, bool value)
        {
            popup.SetValue(DisplayedOnlyMonitorProperty, value);
        }

        public static bool GetDisplayedOnlyMonitor(Popup popup)
        {
            return (bool)popup.GetValue(DisplayedOnlyMonitorProperty);
        }
    }
}
