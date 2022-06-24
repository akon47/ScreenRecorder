using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ScreenRecorder
{
    public partial class MainWindow
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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            HwndSource source = (HwndSource)HwndSource.FromVisual((Window)this);
            source.AddHook(new HwndSourceHook(WndProc));
            windowHandle = source.Handle;

            // 자기 자신은 캡쳐가 안 되도록 하기 위해 사용
            // https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity
            AppManager.Instance.ScreenEncoder.EncoderFirstStarting += (_, __) => UpdateWindowDisplayedOnlyMonitor(AppConfig.Instance.ExcludeFromCapture);
            AppManager.Instance.ScreenEncoder.EncoderStopped += (_, __) => UpdateWindowDisplayedOnlyMonitor(false);
        }

        private void UpdateWindowDisplayedOnlyMonitor(bool excludeFromCapture)
        {
            if (windowHandle == IntPtr.Zero)
                return;

            var isWindowDisplayedOnlyMonitor = Utils.IsWindowDisplayedOnlyMonitor(windowHandle);
            if (excludeFromCapture != isWindowDisplayedOnlyMonitor)
            {
                Utils.SetWindowDisplayedOnlyMonitor(windowHandle, excludeFromCapture);
            }
        }

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

        #region Window Moving
        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
        #endregion
    }
}
