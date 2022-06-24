using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ScreenRecorder.Behaviors
{
    public static class ToolTipBehavior
    {
        public static readonly DependencyProperty IgnoreToolTipWhenEncoderStartedProperty = DependencyProperty.RegisterAttached
        (
            name: "IgnoreToolTipWhenEncoderStarted",
            propertyType: typeof(bool),
            ownerType: typeof(ToolTipBehavior),
            defaultMetadata: new FrameworkPropertyMetadata(false, IgnoreToolTipWhenEncoderStartedPropertyChanged)
        );

        private static void IgnoreToolTipWhenEncoderStartedPropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            Apply(source as FrameworkElement);
        }

        public static bool GetIgnoreToolTipWhenEncoderStarted(DependencyObject obj)
        {
            return (bool)obj.GetValue(IgnoreToolTipWhenEncoderStartedProperty);
        }

        public static void SetIgnoreToolTipWhenEncoderStarted(DependencyObject obj, bool value)
        {
            obj.SetValue(IgnoreToolTipWhenEncoderStartedProperty, value);
        }

        private static void Apply(FrameworkElement element)
        {
            if (element is null)
                throw new ArgumentNullException(nameof(element));

            element.ToolTipOpening -= ElementOnToolTipOpening;
            if (GetIgnoreToolTipWhenEncoderStarted(element))
            {
                element.ToolTipOpening += ElementOnToolTipOpening;
            }
        }

        private static void ElementOnToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (AppManager.Instance.ScreenEncoder.IsStarted)
            {
                // Disable tooltips during capturing
                e.Handled = true;
            }
        }
    }
}
