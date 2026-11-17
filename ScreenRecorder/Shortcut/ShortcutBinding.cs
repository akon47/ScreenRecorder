using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScreenRecorder.Shortcut
{
    /// <summary>
    /// A command the user can trigger with a system-wide keyboard shortcut. Owns the hot-key
    /// registration: changing <see cref="KeyGesture"/> re-registers (or, when null,
    /// unregisters) the global hot key.
    /// </summary>
    public sealed class ShortcutBinding : ObservableObject, IShortcut, IDisposable
    {
        private readonly ICommand _command;
        private GlobalHotKey _hotKey;
        private KeyGesture _keyGesture;

        /// <summary>Stable identifier — the key this shortcut is stored under in the settings file.</summary>
        public string Id { get; }

        /// <summary>Localized name shown in the shortcut editor.</summary>
        public string DisplayName { get; }

        public KeyGesture DefaultKeyGesture { get; }

        public ShortcutBinding(string id, string displayName, ICommand command, KeyGesture defaultKeyGesture = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = displayName;
            _command = command ?? throw new ArgumentNullException(nameof(command));
            DefaultKeyGesture = defaultKeyGesture;
        }

        public KeyGesture KeyGesture
        {
            get => _keyGesture;
            set
            {
                if (!SetProperty(ref _keyGesture, value))
                    return;

                OnPropertyChanged(nameof(KeyGestureString));
                OnPropertyChanged(nameof(Key));
                OnPropertyChanged(nameof(Modifiers));

                _hotKey?.Dispose();
                _hotKey = value != null ? new GlobalHotKey(value, Invoke) : null;
            }
        }

        public string KeyGestureString => KeyGestureFormatter.Format(_keyGesture);

        public Key Key => _keyGesture?.Key ?? Key.None;

        public ModifierKeys Modifiers => _keyGesture?.Modifiers ?? ModifierKeys.None;

        private void Invoke()
        {
            if (_command.CanExecute(null))
                _command.Execute(null);
        }

        public void Dispose()
        {
            _hotKey?.Dispose();
            _hotKey = null;
        }
    }
}
