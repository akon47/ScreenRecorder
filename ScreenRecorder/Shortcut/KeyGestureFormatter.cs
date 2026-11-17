using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace ScreenRecorder.Shortcut
{
    /// <summary>
    /// Display text of a keyboard shortcut. Pure functions — unit tested.
    /// </summary>
    public static class KeyGestureFormatter
    {
        /// <summary>"Ctrl+Shift+F9" — null when there is no gesture.</summary>
        public static string Format(KeyGesture gesture)
        {
            return gesture == null ? null : Format(gesture.Key, gesture.Modifiers);
        }

        public static string Format(Key key, ModifierKeys modifiers)
        {
            return string.Concat(
                modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : string.Empty,
                modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : string.Empty,
                modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : string.Empty,
                modifiers.HasFlag(ModifierKeys.Windows) ? "Win+" : string.Empty,
                KeyToString(key));
        }

        public static string KeyToString(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
                return ((int)(key - Key.D0)).ToString();
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return ((int)(key - Key.NumPad0)).ToString();

            switch (key)
            {
                case Key.Return: return "Enter";
                case Key.Escape: return "Esc";
                case Key.PageUp: return "PageUp";
                case Key.PageDown: return "PageDown";
                case Key.OemOpenBrackets: return "[";
                case Key.OemCloseBrackets: return "]";
                case Key.Oem1: return ";";
                case Key.OemQuestion: return "/";
                case Key.OemQuotes: return "'";
                case Key.Down: return "↓";
                case Key.Up: return "↑";
                case Key.Right: return "→";
                case Key.Left: return "←";
                case Key.Subtract: return "Minus";
                case Key.Add: return "Plus";
                case Key.Multiply: return "*";
                case Key.Divide: return "/";
                case Key.Decimal: return ",";
                case Key.OemComma: return ",";
                case Key.OemPeriod: return ".";
                case Key.OemPipe: return "|";
                default: return key.ToString();
            }
        }
    }
}
