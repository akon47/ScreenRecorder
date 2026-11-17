using System.Windows;

namespace ScreenRecorder.Views
{
    /// <summary>A modal message in the app's own (Fluent) look, replacing the system MessageBox.</summary>
    public partial class MessageDialog : Window
    {
        private MessageDialog(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
        }

        /// <summary>Shows <paramref name="message"/> and waits for OK. <paramref name="owner"/> may be null.</summary>
        public static void Show(Window owner, string message)
        {
            var dialog = new MessageDialog(message);
            if (owner != null)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.ShowDialog();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
