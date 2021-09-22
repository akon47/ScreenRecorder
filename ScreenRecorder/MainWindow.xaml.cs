using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ScreenRecorder.Extensions;

namespace ScreenRecorder
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public int flags;
        }

        private IntPtr windowHandle = IntPtr.Zero;

        public MainWindow()
        {
            InitializeComponent();

            #region Load Window Location
            try
            {
                if (AppConfig.Instance.WindowLeft >= 0 && AppConfig.Instance.WindowTop >= 0)
                {
                    Left = AppConfig.Instance.WindowLeft;
                    Top = AppConfig.Instance.WindowTop;
                }
            }
            catch { }
            #endregion
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            HwndSource source = (HwndSource)HwndSource.FromVisual((Window)this);
            source.AddHook(new HwndSourceHook(WndProc));
            windowHandle = source.Handle;

            // 자기 자신은 캡쳐가 안 되도록 하기 위해 사용
            // https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity
#if !DEBUG
            Utils.SetWindowDisplayedOnlyMonitor(source.Handle, true);
#endif
        }

        #region Window Moving
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            var message = System.Windows.Forms.Message.Create(hwnd, msg, wParam, lParam);

            switch (msg)
            {
                case 0x0046:
                    #region Magnetic Move
                    if (windowHandle != IntPtr.Zero)
                    {
                        WINDOWPOS windowPos = (WINDOWPOS)message.GetLParam(typeof(WINDOWPOS));

                        System.Drawing.Rectangle workingArea = (System.Windows.Forms.Screen.FromHandle(windowHandle)).WorkingArea;

                        int dockMargin = 15;
                        //left
                        if (Math.Abs(windowPos.x - workingArea.Left) <= dockMargin)
                        {
                            windowPos.x = workingArea.Left;
                        }

                        //top
                        if (Math.Abs(windowPos.y - workingArea.Top) <= dockMargin)
                        {
                            windowPos.y = workingArea.Top;
                        }

                        //right
                        if (Math.Abs(windowPos.x + windowPos.cx - workingArea.Left - workingArea.Width) <= dockMargin)
                        {
                            windowPos.x = workingArea.Right - windowPos.cx;
                        }

                        //bottom
                        if (Math.Abs(windowPos.y + windowPos.cy - workingArea.Top - workingArea.Height) <= dockMargin)
                        {
                            windowPos.y = (int)(workingArea.Bottom - windowPos.cy);
                        }
                        Marshal.StructureToPtr(windowPos, lParam, false);
                    }
                    #endregion
                    break;
            }

            return IntPtr.Zero;
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
        #endregion

        #region Commands
        private void CloseCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Close();
        }

        private void CloseCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }
        #endregion

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            AppConfig.Instance.WindowLeft = this.Left;
            AppConfig.Instance.WindowTop = this.Top;
        }
    }
}
