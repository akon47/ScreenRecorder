using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ScreenRecorder.Shortcut
{
    /// <summary>
    /// ShortcutEditor.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class ShortcutEditor : UserControl
    {
        public string Title
		{
			get { return (string)GetValue(TitleProperty); }
			set { SetValue(TitleProperty, value); }
		}
		public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
			"Title", typeof(string), typeof(ShortcutEditor), new PropertyMetadata("Command"));

		public IShortcut Shortcut
		{
			get { return (IShortcut)GetValue(ShortcutProperty); }
			set { SetValue(ShortcutProperty, value); }
		}
		public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(
			"Shortcut", typeof(IShortcut), typeof(ShortcutEditor), new PropertyMetadata(null));

		public bool ShortcutEditable
		{
			get { return (bool)GetValue(ShortcutEditableProperty); }
			set { SetValue(ShortcutEditableProperty, value); }
		}
		public static readonly DependencyProperty ShortcutEditableProperty = DependencyProperty.Register(
			"ShortcutEditable", typeof(bool), typeof(ShortcutEditor), new PropertyMetadata(true));

        public ShortcutEditor()
        {
            InitializeComponent();
        }

        public delegate void OnShortcutChangingHandler(ShortcutEditor sender, CancelEventArgs e, KeyGesture keyGesture);
		public event OnShortcutChangingHandler OnShortcutChanging;

		private static HashSet<Key> ignoredKey = new HashSet<Key>()
		{
			Key.LeftAlt, Key.RightAlt, Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LWin, Key.RWin
		};

		private static HashSet<Key> ignoredAloneKey = new HashSet<Key>()
		{
			Key.Escape
		};
		private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (ShortcutEditable)
			{
				if (!ignoredKey.Contains(e.Key) &&
					(e.Key != Key.System || (e.Key == Key.System && !ignoredKey.Contains(e.SystemKey))))
				{
					Key key = (e.Key == Key.System && !ignoredKey.Contains(e.SystemKey)) ? e.SystemKey : e.Key;
					if (!ignoredAloneKey.Contains(key) ||
						(ignoredAloneKey.Contains(key) && Keyboard.Modifiers != ModifierKeys.None))
					{
						try
						{
							KeyGesture keyGesture = new KeyGesture(key, Keyboard.Modifiers);
							if (Shortcut != null)
							{
								CancelEventArgs cancelEventArgs = new CancelEventArgs() { Cancel = false };
								OnShortcutChanging?.Invoke(this, cancelEventArgs, keyGesture);

								if (!cancelEventArgs.Cancel)
								{
									Shortcut.KeyGesture = keyGesture;
								}
							}
						}
						catch
						{
							// not supported key
						}
					}
				}
			}
			e.Handled = true;
		}

		private void button_deleteShortcut_Click(object sender, RoutedEventArgs e)
		{
			if (Shortcut != null)
			{
				Shortcut.KeyGesture = null;
			}
		}

		private void menuItem_loadDefault_Click(object sender, RoutedEventArgs e)
		{
			if (Shortcut != null)
			{
				if (Shortcut.DefaultKeyGesture != null)
				{
					KeyGesture keyGesture = new KeyGesture(Shortcut.DefaultKeyGesture.Key, Shortcut.DefaultKeyGesture.Modifiers);

					CancelEventArgs cancelEventArgs = new CancelEventArgs();
					cancelEventArgs.Cancel = false;
					OnShortcutChanging?.Invoke(this, cancelEventArgs, keyGesture);

					if (!cancelEventArgs.Cancel)
					{
						Shortcut.KeyGesture = keyGesture;
					}
				}
				else
				{
					Shortcut.KeyGesture = null;
				}
			}
		}
    }
}
