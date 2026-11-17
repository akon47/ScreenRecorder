using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;

namespace ScreenRecorder.Shortcut
{
    /// <summary>
    /// One system-wide hot key (RegisterHotKey) that invokes a callback on the UI thread.
    /// Disposing unregisters it.
    /// </summary>
    public sealed class GlobalHotKey : IDisposable
    {
        /// <summary>
        /// While true, hot keys are not acted on — set while the shortcut editor is open so the
        /// key combination being typed does not also trigger the command it is bound to.
        /// </summary>
        public static bool PassthroughGlobalHotKey { get; set; } = false;

        private static int _nextId;

        private sealed class HotKeyWindow : Control
        {
            private const int WmHotKey = 0x0312;

            private readonly Action _callback;
            private readonly int _id;
            private bool _registered;

            public HotKeyWindow(KeyGesture gesture, Action callback, int id)
            {
                _callback = callback;
                _id = id;

                try
                {
                    _registered = RegisterHotKey(Handle, _id, gesture.Modifiers, (Keys)KeyInterop.VirtualKeyFromKey(gesture.Key));
                }
                catch
                {
                    _registered = false;
                }
            }

            /// <summary>False when another application already owns this key combination.</summary>
            public bool IsRegistered => _registered;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmHotKey && m.WParam.ToInt32() == _id)
                {
                    if (!PassthroughGlobalHotKey)
                    {
                        try
                        {
                            _callback();
                        }
                        catch
                        {
                            // A failing command must not take the message loop down.
                        }
                    }

                    m.Result = new IntPtr(1);
                    return;
                }

                base.WndProc(ref m);
            }

            protected override void Dispose(bool disposing)
            {
                if (_registered && IsHandleCreated)
                {
                    try { UnregisterHotKey(Handle, _id); } catch { }
                    _registered = false;
                }

                base.Dispose(disposing);
            }

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool RegisterHotKey(IntPtr hWnd, int id, ModifierKeys fsModifiers, Keys vk);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        }

        private HotKeyWindow _window;

        public GlobalHotKey(KeyGesture gesture, Action callback)
        {
            if (gesture == null)
                throw new ArgumentNullException(nameof(gesture));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            _window = new HotKeyWindow(gesture, callback, Interlocked.Increment(ref _nextId));
        }

        public bool IsRegistered => _window != null && _window.IsRegistered;

        public void Dispose()
        {
            _window?.Dispose();
            _window = null;
        }
    }
}
