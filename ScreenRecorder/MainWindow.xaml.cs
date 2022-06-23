using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls;

namespace ScreenRecorder
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Button_ToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (AppManager.Instance.ScreenEncoder.IsStarted)
            {
                // Disable tooltips during capturing (main window will already not be captured, s. OnContentRendered)
                e.Handled = true;
            }
        }
    }
}
