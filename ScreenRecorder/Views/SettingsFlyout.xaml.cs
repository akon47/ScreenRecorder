using System;
using System.Windows;
using System.Windows.Input;
using ScreenRecorder.ViewModels;

namespace ScreenRecorder.Views
{
    /// <summary>
    /// The settings flyout window: opens next to the toolbar and closes itself as soon as it
    /// loses focus (light dismiss) or Esc is pressed — except while one of its own dialogs
    /// (folder picker, shortcut editor) is open.
    /// </summary>
    public partial class SettingsFlyout : Window
    {
        private const double Gap = 6;

        private readonly SettingsViewModel _viewModel;
        private readonly ICommand _openShortcutSettings;
        private bool _dialogOpen;
        private bool _closing;

        public SettingsFlyout(SettingsViewModel viewModel, ICommand openShortcutSettings)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _openShortcutSettings = openShortcutSettings;

            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>
        /// Shows the flyout under <paramref name="anchor"/> (above it when there is no room
        /// below), kept inside the anchor's work area.
        /// </summary>
        public void ShowBelow(Window anchor)
        {
            Left = anchor.Left;
            Top = anchor.Top + anchor.ActualHeight + Gap;
            Show();
            Activate();

            // The height is only known once laid out (SizeToContent).
            var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(anchor).Handle);
            var workArea = screen.WorkingArea;

            if (Top + ActualHeight > workArea.Bottom)
            {
                double above = anchor.Top - ActualHeight - Gap;
                Top = above >= workArea.Top ? above : Math.Max(workArea.Top, workArea.Bottom - ActualHeight);
            }

            if (Left + ActualWidth > workArea.Right)
                Left = workArea.Right - ActualWidth;
            if (Left < workArea.Left)
                Left = workArea.Left;
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);

            if (!_dialogOpen && !_closing)
                Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _closing = true;
            base.OnClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Escape && !e.Handled)
            {
                e.Handled = true;
                Close();
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
            => RunDialog(() => _viewModel.BrowseRecordDirectoryCommand.Execute(null));

        private void ShortcutButton_Click(object sender, RoutedEventArgs e)
            => RunDialog(() => _openShortcutSettings?.Execute(null));

        /// <summary>Runs a modal dialog without the resulting focus loss dismissing the flyout.</summary>
        private void RunDialog(Action show)
        {
            _dialogOpen = true;
            try
            {
                show();
            }
            finally
            {
                _dialogOpen = false;
                if (!_closing)
                    Activate();
            }
        }
    }
}
