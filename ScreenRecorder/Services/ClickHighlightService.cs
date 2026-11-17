using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ScreenRecorder.Views;

namespace ScreenRecorder.Services
{
    /// <summary>
    /// Shows a short ring animation wherever the mouse is clicked, so viewers of a recording
    /// can see the clicks. The ring is an ordinary on-screen overlay — it is recorded because
    /// it is on the screen, which also means it appears in display / area recordings but not
    /// when a single window is captured as itself.
    ///
    /// Clicks are observed with a low-level mouse hook. Windows calls such a hook synchronously
    /// for every mouse event in the system, so it lives on its own thread with nothing else to
    /// do: the app's UI thread being busy (finalizing a file, say) can then never make the
    /// mouse stutter. The hook only posts the click position to the UI thread.
    /// </summary>
    public sealed class ClickHighlightService : IDisposable
    {
        private const int WhMouseLl = 14;
        private const int WmLButtonDown = 0x0201;
        private const int WmRButtonDown = 0x0204;
        private const int WmQuit = 0x0012;

        private readonly Dispatcher _dispatcher;
        private readonly LowLevelMouseProc _hookProc; // kept alive: the native side holds only a pointer
        private Thread _thread;
        private uint _threadId;
        private IntPtr _hook;
        private ClickRingWindow _ring;

        public ClickHighlightService()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _hookProc = HookCallback;
        }

        /// <summary>True while clicks are being highlighted.</summary>
        public bool IsActive => _thread != null;

        /// <summary>Starts highlighting clicks (no effect when already started). UI thread.</summary>
        public void Start()
        {
            if (_thread != null)
                return;

            using var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() => HookThread(ready))
            {
                Name = "ClickHighlightHook",
                IsBackground = true,
            };
            _thread.Start();
            ready.Wait(2000);
        }

        /// <summary>Stops highlighting clicks. UI thread.</summary>
        public void Stop()
        {
            Thread thread = _thread;
            if (thread == null)
                return;

            _thread = null;
            if (_threadId != 0)
                PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            thread.Join(1000);

            _ring?.Close();
            _ring = null;
        }

        private void HookThread(ManualResetEventSlim ready)
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WhMouseLl, _hookProc, GetModuleHandle(null), 0);
            ready.Set();

            if (_hook == IntPtr.Zero)
                return;

            try
            {
                // A low-level hook is delivered through this thread's message loop.
                while (GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
            finally
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
                _threadId = 0;
            }
        }

        private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int message = wParam.ToInt32();
                if (message == WmLButtonDown || message == WmRButtonDown)
                {
                    bool rightButton = message == WmRButtonDown;
                    _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => ShowRing(rightButton)));
                }
            }

            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        private void ShowRing(bool rightButton)
        {
            if (_thread == null)
                return;

            // Read the position here, on the UI thread: it is then in the same (possibly
            // DPI-virtualized) coordinates WPF positions windows in.
            System.Drawing.Point position = System.Windows.Forms.Cursor.Position;

            _ring ??= new ClickRingWindow();
            _ring.Play(new Point(position.X, position.Y), rightButton);
        }

        public void Dispose() => Stop();

        #region Interop

        private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr Hwnd;
            public uint Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hookId, LowLevelMouseProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out NativeMessage message, IntPtr hwnd, uint filterMin, uint filterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref NativeMessage message);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref NativeMessage message);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        #endregion
    }
}
