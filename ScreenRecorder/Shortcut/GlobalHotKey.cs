using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using ScreenRecorder.Command;

namespace ScreenRecorder.Shortcut
{
    public sealed class GlobalHotKey : IDisposable
    {
        static public bool PassthroughGlobalHotKey { get; set; } = false;

        internal class HotKeyControl : System.Windows.Forms.Control
        {
            internal class WinApi
            {
                public const int WmHotKey = 0x0312;

                [DllImport("user32.dll", SetLastError = true)]
                public static extern bool RegisterHotKey(IntPtr hWnd, int id, ModifierKeys fsModifiers, Keys vk);

                [DllImport("user32.dll", SetLastError = true)]
                public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
            }

            private DelegateCommand _delegateCommand;
            private int _id;
            private IntPtr _hWnd;
            public HotKeyControl(DelegateCommand delegateCommand, int id)
            {
                _delegateCommand = delegateCommand;
                _id = id;
                RegisterHotKey();
            }

            protected override void WndProc(ref Message m)
            {
                try
                {
                    if (!PassthroughGlobalHotKey)
                    {
                        switch (m.Msg)
                        {
                            case WinApi.WmHotKey:
                                if (m.WParam.ToInt32() == _id)
                                {
                                    _delegateCommand?.Execute();
                                }
                                m.Result = new IntPtr(1);
                                return;
                        }
                    }

                    base.WndProc(ref m);
                }
                catch { }
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                UnregisterHotKey();
            }

            private void RegisterHotKey()
            {
                try
                {
                    if (WinApi.RegisterHotKey(Handle, _id, _delegateCommand.KeyGesture.Modifiers, (System.Windows.Forms.Keys)KeyInterop.VirtualKeyFromKey(_delegateCommand.KeyGesture.Key)))
                    {
                        _hWnd = Handle;
                    }
                }
                catch { }
            }

            private void UnregisterHotKey()
            {
                try
                {
                    if (_hWnd != IntPtr.Zero)
                    {
                        WinApi.UnregisterHotKey(_hWnd, _id);
                    }
                }
                catch { }
            }
        }

        private HotKeyControl _hotKeyControl;
        public GlobalHotKey(DelegateCommand delegateCommand)
        {
            _hotKeyControl = new HotKeyControl(delegateCommand, GetHashCode());
        }

        public void Dispose()
        {
            _hotKeyControl?.Dispose();
            _hotKeyControl = null;
        }
    }
}
