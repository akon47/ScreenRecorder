using System;
using System.Windows;

namespace ScreenRecorder.Behaviors
{
    /// <summary>
    /// DragMove Behavior
    /// </summary>
    public static class DragMoveBehavior
    {
        public static readonly DependencyProperty IsActivatedProperty = DependencyProperty.RegisterAttached
        (
            name: "IsActivated",
            propertyType: typeof(bool),
            ownerType: typeof(DragMoveBehavior),
            defaultMetadata: new FrameworkPropertyMetadata(false, IsActivatedPropertyPropertyChanged)
        );

        private static void IsActivatedPropertyPropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            Apply(source as FrameworkElement, (bool)e.NewValue);
        }

        public static bool GetIsActivated(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsActivatedProperty);
        }

        public static void SetIsActivated(DependencyObject obj, bool value)
        {
            obj.SetValue(IsActivatedProperty, value);
        }

        private static void Apply(FrameworkElement element, bool isActivated)
        {
            if (element is null)
                throw new ArgumentNullException(nameof(element));

            element.MouseDown -= ElementOnMouseDown;

            if (isActivated)
            {
                element.MouseDown += ElementOnMouseDown;
            }
        }

        private static void ElementOnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null)
                return;

            var window = Utils.FindParentWindow(element);
            if (window == null)
                return;

            window.DragMove();
        }
    }
}
