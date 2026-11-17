using System.Collections.Generic;
using System.Windows;

namespace ScreenRecorder.Shortcut
{
    /// <summary>The dialog in which the user assigns key combinations to the global shortcuts.</summary>
    public partial class ShortcutEditorWindow : Window
    {
        public ShortcutEditorWindow(IReadOnlyList<ShortcutBinding> shortcuts)
        {
            InitializeComponent();
            ShortcutList.ItemsSource = shortcuts;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
