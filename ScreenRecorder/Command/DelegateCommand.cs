using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ScreenRecorder.Config;
using ScreenRecorder.Shortcut;

namespace ScreenRecorder.Command
{
    public class DelegateCommand : NotifyPropertyBase, ICommand, IShortcut, IConfig
    {
        private GlobalHotKey globalHotKey;

        #region Property backing fields

        private readonly object SyncLock = new object();
        private readonly Func<object, bool> m_CanExecute;
        private readonly Action<object> ExecuteAction;
        private bool m_IsExecuting;

        #region 생성자

        public DelegateCommand(Action<object> execute, Func<object, bool> canExecute, KeyGesture keyGesture = null)
        {
            var callback = execute ?? throw new ArgumentNullException(nameof(execute));
            m_CanExecute = canExecute;

            ExecuteAction = parameter =>
            {
                try
                {
                    var canExecuteAction = m_CanExecute?.Invoke(parameter) ?? true;

                    if (canExecuteAction)
                    {
                        callback(parameter);
                    }
                }
                catch { }
            };

            this.keyGesture = keyGesture;
        }

        public DelegateCommand(Action<object> execute)
            : this(execute, null)
        {
        }

        #endregion

        #region Events

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        #endregion

        #endregion

        #region ICommand Members

        public bool IsExecuting
        {
            get
            {
                lock (SyncLock)
                {
                    return m_IsExecuting;
                }
            }
            private set
            {
                lock (SyncLock)
                {
                    m_IsExecuting = value;
                }
            }
        }

        private KeyGesture keyGesture;

        public KeyGesture KeyGesture
        {
            get => keyGesture;
            set
            {
                if (SetProperty(ref keyGesture, value))
                {
                    NotifyPropertyChanged(nameof(KeyGestureString), nameof(Key), nameof(Modifiers));

                    if (keyGesture != null)
                    {
                        globalHotKey?.Dispose();
                        globalHotKey = new GlobalHotKey(this);
                    }
                    else
                    {
                        globalHotKey?.Dispose();
                        globalHotKey = null;
                    }
                }
            }
        }

        public string KeyGestureString
        {
            get
            {
                if (keyGesture != null)
                {
                    return string.Format("{0}{1}{2}{3}",
                        keyGesture.Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : string.Empty,
                        keyGesture.Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : string.Empty,
                        keyGesture.Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : string.Empty,
                        KeyToString(keyGesture.Key));
                }

                return null;
            }
        }

        public Key Key
        {
            get
            {
                if (keyGesture != null)
                {
                    return keyGesture.Key;
                }

                return Key.None;
            }
        }

        public ModifierKeys Modifiers
        {
            get
            {
                if (keyGesture != null)
                {
                    return keyGesture.Modifiers;
                }

                return ModifierKeys.None;
            }
        }

        public KeyGesture DefaultKeyGesture { get; } = null;

        private string KeyToString(Key key)
        {
            var ret = key.ToString();

            switch (key)
            {
                case Key.NumPad0:
                case Key.D0:
                    ret = "0";
                    break;
                case Key.NumPad1:
                case Key.D1:
                    ret = "1";
                    break;
                case Key.NumPad2:
                case Key.D2:
                    ret = "2";
                    break;
                case Key.NumPad3:
                case Key.D3:
                    ret = "3";
                    break;
                case Key.NumPad4:
                case Key.D4:
                    ret = "4";
                    break;
                case Key.NumPad5:
                case Key.D5:
                    ret = "5";
                    break;
                case Key.NumPad6:
                case Key.D6:
                    ret = "6";
                    break;
                case Key.NumPad7:
                case Key.D7:
                    ret = "7";
                    break;
                case Key.NumPad8:
                case Key.D8:
                    ret = "8";
                    break;
                case Key.NumPad9:
                case Key.D9:
                    ret = "9";
                    break;
                case Key.Return:
                    ret = "Enter";
                    break;
                case Key.Escape:
                    ret = "Esc";
                    break;
                case Key.PageUp:
                    ret = "PageUp";
                    break;
                case Key.PageDown:
                    ret = "PageDown";
                    break;
                case Key.OemOpenBrackets:
                    ret = "[";
                    break;
                case Key.OemCloseBrackets:
                    ret = "]";
                    break;
                case Key.Oem1:
                    ret = ";";
                    break;
                case Key.OemQuestion:
                    ret = "/";
                    break;
                case Key.OemQuotes:
                    ret = "'";
                    break;
                case Key.Down:
                    ret = "↓";
                    break;
                case Key.Up:
                    ret = "↑";
                    break;
                case Key.Right:
                    ret = "→";
                    break;
                case Key.Left:
                    ret = "←";
                    break;
                case Key.Subtract:
                    ret = "Minus";
                    break;
                case Key.Add:
                    ret = "Plus";
                    break;
                case Key.Multiply:
                    ret = "*";
                    break;
                case Key.Divide:
                    ret = "/";
                    break;
                case Key.Decimal:
                    ret = ",";
                    break;
                case Key.OemComma:
                    ret = ",";
                    break;
                case Key.OemPeriod:
                    ret = ".";
                    break;
                case Key.OemPipe:
                    ret = "|";
                    break;
            }

            return ret;
        }

        [DebuggerStepThrough]
        public bool CanExecute(object parameter)
        {
            if (IsExecuting)
            {
                return false;
            }

            try
            {
                return m_CanExecute == null || m_CanExecute(parameter);
            }
            catch
            {
                return false;
            }
        }

        public bool CanExecute()
        {
            return CanExecute(null);
        }

        public void Execute(object parameter)
        {
            ExecuteAsync(parameter);
        }

        public void Execute()
        {
            ExecuteAsync(null);
        }

        public bool TryExecute(object parameter = null)
        {
            if (CanExecute(parameter))
            {
                if (IsExecuting)
                {
                    return false;
                }

                ExecuteAsync(parameter);
                return true;
            }

            return false;
        }

        public ConfiguredTaskAwaitable ExecuteAsync(object parameter)
        {
            return Task.Run(async () =>
            {
                if (IsExecuting)
                {
                    return;
                }

                try
                {
                    IsExecuting = true;
                    await Application.Current.Dispatcher.BeginInvoke(ExecuteAction, DispatcherPriority.Normal,
                        parameter);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not execute command. {ex.Message}");
                    throw;
                }
                finally
                {
                    IsExecuting = false;
                    CommandManager.InvalidateRequerySuggested();
                }
            }).ConfigureAwait(true);
        }

        public ConfiguredTaskAwaitable ExecuteAsync()
        {
            return ExecuteAsync(null);
        }

        public virtual Dictionary<string, string> SaveConfig()
        {
            var dicConfig = new Dictionary<string, string>();
            if (KeyGesture != null)
            {
                dicConfig.Add("Key", Enum.GetName(typeof(Key), Key));
                dicConfig.Add("Control", Modifiers.HasFlag(ModifierKeys.Control).ToString());
                dicConfig.Add("Alt", Modifiers.HasFlag(ModifierKeys.Alt).ToString());
                dicConfig.Add("Shift", Modifiers.HasFlag(ModifierKeys.Shift).ToString());
                dicConfig.Add("Windows", Modifiers.HasFlag(ModifierKeys.Windows).ToString());
            }

            return dicConfig;
        }

        public virtual void LoadConfig(Dictionary<string, string> config)
        {
            if (config != null)
            {
                if (config.ContainsKey("Key"))
                {
                    var key = (Key)Enum.Parse(typeof(Key),
                        Config.Config.LoadConfigItem(config, "Key", Enum.GetName(typeof(Key), Key.None)));
                    if (key != Key.None)
                    {
                        var control = bool.Parse(Config.Config.LoadConfigItem(config, "Control", "false"));
                        var alt = bool.Parse(Config.Config.LoadConfigItem(config, "Alt", "false"));
                        var shift = bool.Parse(Config.Config.LoadConfigItem(config, "Shift", "false"));
                        var windows = bool.Parse(Config.Config.LoadConfigItem(config, "Windows", "false"));

                        var modifiers =
                            (control ? ModifierKeys.Control : ModifierKeys.None) |
                            (alt ? ModifierKeys.Alt : ModifierKeys.None) |
                            (shift ? ModifierKeys.Shift : ModifierKeys.None) |
                            (windows ? ModifierKeys.Windows : ModifierKeys.None);
                        KeyGesture = new KeyGesture(key, modifiers);
                    }
                    else
                    {
                        KeyGesture = null;
                    }
                }
                else
                {
                    KeyGesture = DefaultKeyGesture;
                }
            }
            else
            {
                KeyGesture = DefaultKeyGesture;
            }
        }

        #endregion
    }
}
