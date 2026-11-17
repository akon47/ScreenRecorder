using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenRecorder.Shortcut
{
    /// <summary>
    /// Edits one <see cref="IShortcut"/>: while the box has focus, the next key combination
    /// pressed becomes the shortcut.
    /// </summary>
    public partial class ShortcutEditor : UserControl
    {
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(ShortcutEditor), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(
            nameof(Shortcut), typeof(IShortcut), typeof(ShortcutEditor), new PropertyMetadata(null));

        public static readonly DependencyProperty ShortcutEditableProperty = DependencyProperty.Register(
            nameof(ShortcutEditable), typeof(bool), typeof(ShortcutEditor), new PropertyMetadata(true));

        /// <summary>Modifier keys on their own are not a shortcut.</summary>
        private static readonly HashSet<Key> ModifierOnlyKeys = new HashSet<Key>
        {
            Key.LeftAlt, Key.RightAlt, Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LWin, Key.RWin,
        };

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public IShortcut Shortcut
        {
            get => (IShortcut)GetValue(ShortcutProperty);
            set => SetValue(ShortcutProperty, value);
        }

        public bool ShortcutEditable
        {
            get => (bool)GetValue(ShortcutEditableProperty);
            set => SetValue(ShortcutEditableProperty, value);
        }

        public ShortcutEditor()
        {
            InitializeComponent();
        }

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Alt combinations arrive as Key.System with the real key in SystemKey.
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Let Tab move between rows and a bare Esc close the dialog.
            if (Keyboard.Modifiers == ModifierKeys.None && (key == Key.Tab || key == Key.Escape))
                return;

            e.Handled = true;

            if (!ShortcutEditable || Shortcut == null || ModifierOnlyKeys.Contains(key))
                return;

            try
            {
                Shortcut.KeyGesture = new KeyGesture(key, Keyboard.Modifiers);
            }
            catch (NotSupportedException)
            {
                // WPF does not accept this combination as a gesture (e.g. a bare letter).
            }
        }

        private void button_deleteShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (Shortcut != null)
                Shortcut.KeyGesture = null;
        }
    }
}
