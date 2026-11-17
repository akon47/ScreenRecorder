using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ScreenRecorder.Behaviors
{
    /// <summary>
    /// Clicking anywhere outside a text input takes keyboard focus away from it. Most of the
    /// app's buttons are deliberately not focusable (so they never steal focus from the app
    /// being recorded), which also means clicking them would leave a text box focused forever —
    /// caret blinking, still swallowing key presses. Set
    /// <c>behaviors:ClearFocusOnClickBehavior.IsActivated="True"</c> on a window's root element.
    /// </summary>
    public static class ClearFocusOnClickBehavior
    {
        public static readonly DependencyProperty IsActivatedProperty = DependencyProperty.RegisterAttached(
            "IsActivated", typeof(bool), typeof(ClearFocusOnClickBehavior), new FrameworkPropertyMetadata(false, OnIsActivatedChanged));

        public static bool GetIsActivated(DependencyObject obj) => (bool)obj.GetValue(IsActivatedProperty);

        public static void SetIsActivated(DependencyObject obj, bool value) => obj.SetValue(IsActivatedProperty, value);

        private static void OnIsActivatedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is UIElement element))
                return;

            element.PreviewMouseDown -= OnPreviewMouseDown;
            if ((bool)e.NewValue)
                element.PreviewMouseDown += OnPreviewMouseDown;
        }

        private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!(Keyboard.FocusedElement is TextBoxBase) && !(Keyboard.FocusedElement is PasswordBox))
                return;

            // A click inside the focused input (or any other text input) keeps / moves focus normally.
            if (IsInsideTextInput(e.OriginalSource as DependencyObject))
                return;

            DependencyObject scope = FocusManager.GetFocusScope((DependencyObject)sender);
            FocusManager.SetFocusedElement(scope, null);
            Keyboard.ClearFocus();
        }

        private static bool IsInsideTextInput(DependencyObject element)
        {
            while (element != null)
            {
                if (element is TextBoxBase || element is PasswordBox)
                    return true;

                element = element is Visual || element is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }

            return false;
        }
    }
}
