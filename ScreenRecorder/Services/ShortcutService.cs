using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;
using ScreenRecorder.Shortcut;

namespace ScreenRecorder.Services
{
    /// <summary>
    /// The app's system-wide keyboard shortcuts: which command each one triggers and which key
    /// combination the user assigned. A view model registers its commands
    /// (<see cref="Register"/>); the saved combination — or the default — is applied at once,
    /// and every later change is written back to the settings.
    /// </summary>
    public sealed class ShortcutService : IDisposable
    {
        // Shortcut ids are keys in settings.json: never rename them.
        public const string StartRecordingId = "startRecording";
        public const string PauseRecordingId = "pauseRecording";
        public const string StopRecordingId = "stopRecording";
        public const string RegionRecordingId = "regionRecording";
        public const string GifRecordingId = "gifRecording";

        private readonly List<ShortcutBinding> _shortcuts = new List<ShortcutBinding>();
        private readonly SettingsStore _store;
        private bool _disposed;

        public IReadOnlyList<ShortcutBinding> Shortcuts => _shortcuts;

        public ShortcutService(SettingsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Adds a shortcut for <paramref name="command"/> and activates its saved (or default)
        /// key combination.
        /// </summary>
        public ShortcutBinding Register(string id, string displayName, ICommand command, KeyGesture defaultKeyGesture = null)
        {
            var binding = new ShortcutBinding(id, displayName, command, defaultKeyGesture);

            binding.KeyGesture = _store.Settings.Shortcuts.TryGetValue(id, out ShortcutSetting saved) && saved != null
                ? saved.ToGesture()
                : defaultKeyGesture;

            binding.PropertyChanged += OnShortcutChanged;
            _shortcuts.Add(binding);
            return binding;
        }

        private void OnShortcutChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ShortcutBinding.KeyGesture) || !(sender is ShortcutBinding binding))
                return;

            _store.Update(settings => settings.Shortcuts[binding.Id] = ShortcutSetting.FromGesture(binding.KeyGesture));
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            foreach (var shortcut in _shortcuts)
            {
                shortcut.PropertyChanged -= OnShortcutChanged;
                shortcut.Dispose();
            }
        }
    }
}
