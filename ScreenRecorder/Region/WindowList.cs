using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenRecorder.Region
{
    /// <summary>A window that can be picked from a list to be recorded.</summary>
    public sealed class WindowListItem
    {
        public IntPtr Handle { get; }
        public string Title { get; }

        /// <summary>The window's small icon; null when it has none.</summary>
        public ImageSource Icon { get; }

        public WindowListItem(IntPtr handle, string title, ImageSource icon)
        {
            Handle = handle;
            Title = title;
            Icon = icon;
        }
    }

    /// <summary>
    /// The open windows as the user knows them — what Alt+Tab or the taskbar shows — for
    /// choosing a window to record by name. Unlike picking on screen, this also reaches
    /// windows that are completely covered by others.
    /// </summary>
    public static class WindowList
    {
        /// <summary>Longest title shown; longer ones are cut with an ellipsis.</summary>
        public const int MaximumTitleLength = 60;

        private const int GwlExStyle = -20;
        private const int GwOwner = 4;
        private const long WsExToolWindow = 0x00000080;
        private const long WsExAppWindow = 0x00040000;
        private const long WsExNoActivate = 0x08000000;

        /// <summary>The recordable windows, front to back.</summary>
        public static IReadOnlyList<WindowListItem> GetWindows()
        {
            var windows = new List<WindowListItem>();
            uint ownProcessId = (uint)Environment.ProcessId;

            EnumWindows((hwnd, lparam) =>
            {
                if (!WindowRegion.IsCapturable(hwnd, ownProcessId))
                    return true;

                string title = GetTitle(hwnd);
                long exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
                bool hasOwner = GetWindow(hwnd, GwOwner) != IntPtr.Zero;
                if (!IsListed(title, exStyle, hasOwner))
                    return true;

                System.Windows.Rect? bounds = WindowRegion.GetVirtualBounds(hwnd);
                if (bounds == null || bounds.Value.Width <= 16 || bounds.Value.Height <= 16)
                    return true;

                windows.Add(new WindowListItem(hwnd, ShortenTitle(title), GetIcon(hwnd)));
                return true;
            }, IntPtr.Zero);

            return windows;
        }

        /// <summary>
        /// Whether a window belongs in the list: the same rule the taskbar uses. A titled
        /// top-level window is listed unless it is a tool window or owned by another window —
        /// and either of those is listed after all when it asks for a taskbar button.
        /// </summary>
        public static bool IsListed(string title, long exStyle, bool hasOwner)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            if ((exStyle & WsExAppWindow) != 0)
                return true;

            if ((exStyle & (WsExToolWindow | WsExNoActivate)) != 0)
                return false;

            return !hasOwner;
        }

        /// <summary>A title that fits a menu: one line, cut with an ellipsis when too long.</summary>
        public static string ShortenTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return string.Empty;

            string singleLine = title.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            return singleLine.Length <= MaximumTitleLength
                ? singleLine
                : singleLine.Substring(0, MaximumTitleLength - 1).TrimEnd() + "…";
        }

        private static string GetTitle(IntPtr hwnd)
        {
            int length = GetWindowTextLength(hwnd);
            if (length <= 0)
                return string.Empty;

            var text = new StringBuilder(length + 1);
            GetWindowText(hwnd, text, text.Capacity);
            return text.ToString();
        }

        private static ImageSource GetIcon(IntPtr hwnd)
        {
            try
            {
                IntPtr icon = IntPtr.Zero;

                // Ask the window, but never wait for one that does not answer.
                foreach (int kind in new[] { IconSmall2, IconSmall, IconBig })
                {
                    if (SendMessageTimeout(hwnd, WmGetIcon, new IntPtr(kind), IntPtr.Zero, SmtoAbortIfHung, 50, out icon) != IntPtr.Zero && icon != IntPtr.Zero)
                        break;
                    icon = IntPtr.Zero;
                }

                if (icon == IntPtr.Zero)
                    icon = GetClassLongPtr(hwnd, GclpHIconSm);
                if (icon == IntPtr.Zero)
                    icon = GetClassLongPtr(hwnd, GclpHIcon);
                if (icon == IntPtr.Zero)
                    return null;

                // The handle belongs to the other window: copy the picture, do not destroy it.
                BitmapSource image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(16, 16));
                image.Freeze();
                return image;
            }
            catch
            {
                return null; // an icon is decoration; the window is listed without one
            }
        }

        #region Interop

        private const int WmGetIcon = 0x007F;
        private const int IconSmall = 0;
        private const int IconBig = 1;
        private const int IconSmall2 = 2;
        private const int GclpHIcon = -14;
        private const int GclpHIconSm = -34;
        private const uint SmtoAbortIfHung = 0x0002;

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lparam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lparam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hwnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
        private static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

        #endregion
    }
}
