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
            if(source is Popup popup)
            {
                if(e.NewValue is bool displayedOnlyMonitor)
                {
                    if(displayedOnlyMonitor)
                    {
                        popup.Opened += Popup_Opened;
                    }
                    else
                    {
                        popup.Opened -= Popup_Opened;
                    }
                }
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
#if !DEBUG
                        Utils.SetWindowDisplayedOnlyMonitor(popupWindowHandle, true);
#endif
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
