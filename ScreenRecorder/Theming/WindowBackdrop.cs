using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace ScreenRecorder.Theming
{
    /// <summary>The Windows 11 system material behind a window.</summary>
    public enum BackdropKind
    {
        /// <summary>No system material: the window paints its own solid background.</summary>
        None,

        /// <summary>Mica — the opaque, wallpaper-tinted material of long-lived app windows.</summary>
        Mica,

        /// <summary>Acrylic — the translucent blur of transient surfaces (flyouts, menus).</summary>
        Acrylic,
    }

    /// <summary>
    /// Gives WPF windows and popups the Windows 11 frame: system backdrop (Mica / Acrylic),
    /// rounded corners with the system shadow, and a dark or light frame that follows the app
    /// theme. Everything degrades by itself: where the OS has no system backdrop (Windows 10,
    /// Windows 11 before 22H2, high contrast) the surface keeps an opaque themed background,
    /// and where transparency effects are off or the session is remote, DWM itself renders the
    /// material as a solid color.
    ///
    /// Usage: <c>theming:WindowBackdrop.Kind="Mica"</c> on a Window, or
    /// <c>theming:WindowBackdrop.Kind="Acrylic"</c> on a Popup. The element's content then
    /// uses <c>{DynamicResource WindowBackgroundBrush}</c> / <c>FlyoutBackgroundBrush</c>, which
    /// the theme makes translucent only when the material is really there
    /// (see <see cref="IsBackdropSupported"/>).
    /// </summary>
    public static class WindowBackdrop
    {
        /// <summary>DWMWA_SYSTEMBACKDROP_TYPE needs Windows 11 22H2 (build 22621).</summary>
        public static bool IsBackdropSupported { get; } = Environment.OSVersion.Version.Build >= 22621;

        /// <summary>Rounded corners (DWMWA_WINDOW_CORNER_PREFERENCE) need Windows 11 (build 22000).</summary>
        public static bool IsRoundedCornersSupported { get; } = Environment.OSVersion.Version.Build >= 22000;

        public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
            "Kind", typeof(BackdropKind), typeof(WindowBackdrop), new PropertyMetadata(BackdropKind.None, OnKindChanged));

        public static BackdropKind GetKind(DependencyObject obj) => (BackdropKind)obj.GetValue(KindProperty);

        public static void SetKind(DependencyObject obj, BackdropKind value) => obj.SetValue(KindProperty, value);

        /// <summary>
        /// True when the material is actually shown: supported by the OS, not in high contrast,
        /// and windows are GPU-rendered (the transparent surface a backdrop needs is not
        /// reliable in software rendering).
        /// </summary>
        public static bool IsMaterialActive
            => IsBackdropSupported && ThemeManager.Current != AppTheme.HighContrast && !RenderModeHelper.IsSoftwareRendering;

        private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Window window)
            {
                window.SourceInitialized -= Window_SourceInitialized;
                window.SourceInitialized += Window_SourceInitialized;
                if (PresentationSource.FromVisual(window) is HwndSource source)
                    ApplyToWindow(window, source);
            }
            else if (d is Popup popup)
            {
                popup.Opened -= Popup_Opened;
                popup.Opened += Popup_Opened;
            }
        }

        private static void Window_SourceInitialized(object sender, EventArgs e)
        {
            var window = (Window)sender;
            if (PresentationSource.FromVisual(window) is HwndSource source)
            {
                ApplyToWindow(window, source);

                // A backdrop chosen before the window is on screen is not composed until
                // something resizes the window. Choose it again once the window is shown.
                EventHandler rendered = null;
                rendered = (s, a) =>
                {
                    window.ContentRendered -= rendered;
                    if (PresentationSource.FromVisual(window) is HwndSource shown && !shown.IsDisposed)
                        StartMaterial(shown, GetKind(window));
                };
                window.ContentRendered += rendered;

                // Re-apply when the system theme flips while the window is open.
                EventHandler handler = null;
                handler = (s, a) =>
                {
                    if (PresentationSource.FromVisual(window) is HwndSource live && !live.IsDisposed)
                        ApplyToWindow(window, live);
                };
                ThemeManager.ThemeChanged += handler;
                window.Closed += (s, a) => ThemeManager.ThemeChanged -= handler;
            }
        }

        /// <summary>
        /// Makes DWM start composing the material of a window that is now on screen, by
        /// setting the backdrop type afresh (setting the same value again is ignored, hence
        /// the detour through "none"). As everywhere in Windows 11, the material shows while
        /// the window is active; an inactive window gets the flat fallback color from DWM.
        /// </summary>
        private static void StartMaterial(HwndSource source, BackdropKind kind)
        {
            if (kind == BackdropKind.None || !IsMaterialActive)
                return;

            IntPtr hwnd = source.Handle;
            NativeMethods.SetIntAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, NativeMethods.DWMSBT_NONE);
            NativeMethods.SetIntAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE,
                kind == BackdropKind.Mica ? NativeMethods.DWMSBT_MAINWINDOW : NativeMethods.DWMSBT_TRANSIENTWINDOW);
        }

        private static void Popup_Opened(object sender, EventArgs e)
        {
            var popup = (Popup)sender;
            if (popup.Child != null && PresentationSource.FromVisual(popup.Child) is HwndSource source)
                ApplyFrame(source, GetKind(popup), smallCorners: false);
        }

        private static void ApplyToWindow(Window window, HwndSource source)
        {
            ApplyFrame(source, GetKind(window), smallCorners: false);
        }

        /// <summary>
        /// Applies the frame to any top-level HWND WPF owns (window or popup). Safe to call
        /// repeatedly.
        /// </summary>
        public static void ApplyFrame(HwndSource source, BackdropKind kind, bool smallCorners)
        {
            if (source == null || source.IsDisposed)
                return;

            IntPtr hwnd = source.Handle;
            if (hwnd == IntPtr.Zero)
                return;

            NativeMethods.SetIntAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ThemeManager.IsDark ? 1 : 0);

            if (IsRoundedCornersSupported)
            {
                NativeMethods.SetIntAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                    smallCorners ? NativeMethods.DWMWCP_ROUNDSMALL : NativeMethods.DWMWCP_ROUND);
            }

            bool material = kind != BackdropKind.None && IsMaterialActive;
            if (source.CompositionTarget != null)
                source.CompositionTarget.BackgroundColor = material ? Colors.Transparent : SystemBackgroundColor();

            if (!IsBackdropSupported)
                return;

            if (material)
            {
                // The backdrop is drawn in the (extended) frame area: extend it under the whole
                // client area so the transparent WPF surface shows it everywhere.
                var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
                NativeMethods.SetIntAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE,
                    kind == BackdropKind.Mica ? NativeMethods.DWMSBT_MAINWINDOW : NativeMethods.DWMSBT_TRANSIENTWINDOW);
            }
            else
            {
                NativeMethods.SetIntAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, NativeMethods.DWMSBT_NONE);
            }
        }

        private static Color SystemBackgroundColor()
        {
            if (Application.Current?.TryFindResource("SolidBackgroundFillColorBaseBrush") is SolidColorBrush brush)
                return brush.Color;
            return ThemeManager.IsDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);
        }
    }
}
