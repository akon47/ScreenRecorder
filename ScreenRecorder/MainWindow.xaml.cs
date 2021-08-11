using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;

namespace ScreenRecorder
{
	/// <summary>
	/// MainWindow.xaml에 대한 상호 작용 논리
	/// </summary>
	public partial class MainWindow : Window
	{
		[StructLayout(LayoutKind.Sequential)]
		public struct WINDOWPOS
		{
			public IntPtr hwnd;
			public IntPtr hwndInsertAfter;
			public int x;
			public int y;
			public int cx;
			public int cy;
			public int flags;
		}

		private IntPtr windowHandle = IntPtr.Zero;

		public MainWindow()
		{
			InitializeComponent();
		}

		protected override void OnSourceInitialized(EventArgs e)
		{
			base.OnSourceInitialized(e);

			HwndSource source = (HwndSource)HwndSource.FromVisual((Window)this);
			source.AddHook(new HwndSourceHook(WndProc));
			windowHandle = source.Handle;
			Utils.SetWindowDisplayAffinity(source.Handle, true);
		}

		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			var message = System.Windows.Forms.Message.Create(hwnd, msg, wParam, lParam);

			switch (msg)
			{
				case 0x0046:
					#region Magnetic Move
					if (windowHandle != IntPtr.Zero)
					{
						WINDOWPOS windowPos = (WINDOWPOS)message.GetLParam(typeof(WINDOWPOS));

						System.Drawing.Rectangle workingArea = (System.Windows.Forms.Screen.FromHandle(windowHandle)).WorkingArea;

						int dockMargin = 15;
						//left
						if (Math.Abs(windowPos.x - workingArea.Left) <= dockMargin)
						{
							windowPos.x = workingArea.Left;
						}
 
						//top
						if (Math.Abs(windowPos.y - workingArea.Top) <= dockMargin)
						{
							windowPos.y = workingArea.Top;
						}
 
						//right
						if (Math.Abs(windowPos.x + windowPos.cx - workingArea.Left - workingArea.Width) <= dockMargin)
						{
							windowPos.x = workingArea.Right - windowPos.cx;
						}
 
						//bottom
						if (Math.Abs(windowPos.y + windowPos.cy - workingArea.Top - workingArea.Height) <= dockMargin)
						{
							windowPos.y = (int)(workingArea.Bottom - windowPos.cy);
						}
						Marshal.StructureToPtr(windowPos, lParam, false);
					}
					#endregion
					break;
			}

			return IntPtr.Zero;
		}

		#region Window Moving
		private void Border_MouseDown(object sender, MouseButtonEventArgs e)
		{
			if(e.ChangedButton == MouseButton.Left)
				DragMove();
		}
		#endregion

		#region Commands
		private void CloseCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
			Close();
        }

        private void CloseCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
			e.CanExecute = true;
        }
		#endregion
	}
}
