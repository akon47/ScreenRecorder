using System.Windows;
using System.Windows.Input;

namespace ScreenRecorder.Behaviors
{
    /// <summary>
    /// Lets a borderless window be dragged by an element: set
    /// <c>behaviors:DragMoveBehavior.IsActivated="True"</c> on it and a left-button press that
    /// no child control handled starts moving the window.
    /// </summary>
    public static class DragMoveBehavior
    {
        public static readonly DependencyProperty IsActivatedProperty = DependencyProperty.RegisterAttached(
            "IsActivated", typeof(bool), typeof(DragMoveBehavior), new FrameworkPropertyMetadata(false, OnIsActivatedChanged));

        public static bool GetIsActivated(DependencyObject obj) => (bool)obj.GetValue(IsActivatedProperty);

        public static void SetIsActivated(DependencyObject obj, bool value) => obj.SetValue(IsActivatedProperty, value);

        private static void OnIsActivatedChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            if (!(source is UIElement element))
                return;

            element.MouseLeftButtonDown -= OnMouseLeftButtonDown;
            if ((bool)e.NewValue)
                element.MouseLeftButtonDown += OnMouseLeftButtonDown;
        }

        private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // DragMove throws unless the left button is (still) down.
            if (e.ButtonState != MouseButtonState.Pressed || !(sender is DependencyObject element))
                return;

            Window.GetWindow(element)?.DragMove();
        }
    }
}
