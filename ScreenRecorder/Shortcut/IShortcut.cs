using System.Windows.Input;

namespace ScreenRecorder.Shortcut
{
    public interface IShortcut
    {
        KeyGesture KeyGesture { get; set; }
        string KeyGestureString { get; }
        Key Key { get; }
        ModifierKeys Modifiers { get; }
        KeyGesture DefaultKeyGesture { get; }
    }
}
