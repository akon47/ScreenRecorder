using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ScreenRecorder.Theming
{
    /// <summary>The palette family in use.</summary>
    public enum AppTheme
    {
        Light,
        Dark,
        HighContrast,
    }

    /// <summary>
    /// Follows the Windows personalization settings: app light/dark mode, the accent color and
    /// high contrast. The Fluent color tokens live in one resource dictionary per theme; this
    /// class swaps that dictionary and injects the accent brushes, and every style refers to the
    /// tokens through <c>DynamicResource</c>, so a change in Windows settings re-skins the
    /// running app instantly.
    /// </summary>
    public static class ThemeManager
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string DwmKey = @"Software\Microsoft\Windows\DWM";

        private static ResourceDictionary _tokens;
        private static bool _initialized;

        /// <summary>The theme currently applied.</summary>
        public static AppTheme Current { get; private set; } = AppTheme.Dark;

        public static bool IsDark => Current == AppTheme.Dark;

        /// <summary>Raised on the UI thread after the palette was swapped.</summary>
        public static event EventHandler ThemeChanged;

        /// <summary>Applies the current system theme and starts following changes.</summary>
        public static void Initialize(Application app)
        {
            if (_initialized)
                return;
            _initialized = true;

            Apply(app);
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General
                    || e.Category == UserPreferenceCategory.Color
                    || e.Category == UserPreferenceCategory.Accessibility
                    || e.Category == UserPreferenceCategory.VisualStyle)
                {
                    app.Dispatcher.BeginInvoke(new Action(() => Apply(app)));
                }
            };
        }

        /// <summary>
        /// Re-applies the surfaces and window frames although the theme itself did not change
        /// (e.g. after switching between GPU and software rendering, which decides whether the
        /// system backdrop can be used).
        /// </summary>
        public static void Refresh(Application app)
        {
            Apply(app);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Re-reads the system settings and re-applies the palette if anything changed.</summary>
        public static void Apply(Application app)
        {
            AppTheme theme = DetectTheme();
            Color accent = DetectAccentColor();

            var merged = app.Resources.MergedDictionaries;

            // App.xaml lists a token dictionary first (for the designer / first frame); that is
            // the slot this class owns. Later dictionaries win lookups, so the active tokens
            // must be the ONLY token dictionary present.
            _tokens ??= FindTokenDictionary(merged);

            bool themeChanged = _tokens == null || theme != Current || !IsTokensFor(_tokens, theme);
            if (themeChanged)
            {
                var tokens = new ResourceDictionary { Source = TokensUri(theme) };
                int index = _tokens != null ? merged.IndexOf(_tokens) : -1;
                if (index >= 0)
                    merged[index] = tokens;
                else
                    merged.Insert(0, tokens);

                _tokens = tokens;
                Current = theme;
            }

            ApplyAccent(app.Resources, theme, accent);
            ApplySurfaces(app.Resources);

            if (themeChanged)
                ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static ResourceDictionary FindTokenDictionary(System.Collections.ObjectModel.Collection<ResourceDictionary> merged)
        {
            foreach (var dictionary in merged)
            {
                if (dictionary.Source != null && dictionary.Source.OriginalString.Contains("Tokens."))
                    return dictionary;
            }

            return null;
        }

        private static bool IsTokensFor(ResourceDictionary dictionary, AppTheme theme)
            => dictionary.Source != null && dictionary.Source.OriginalString.Contains("Tokens." + theme);

        /// <summary>
        /// Window and flyout background brushes. With a system backdrop (Mica / Acrylic) behind
        /// the window they are (almost) transparent so the material shows; without one they
        /// are the theme's solid surface colors.
        /// </summary>
        private static void ApplySurfaces(ResourceDictionary resources)
        {
            if (WindowBackdrop.IsMaterialActive)
            {
                resources["WindowBackgroundBrush"] = Frozen(Colors.Transparent);
                resources["FlyoutBackgroundBrush"] = resources["AcrylicTintFillColorBrush"];
            }
            else
            {
                resources["WindowBackgroundBrush"] = resources["SolidBackgroundFillColorBaseBrush"];
                resources["FlyoutBackgroundBrush"] = resources["AcrylicBackgroundFillColorDefaultBrush"];
            }
        }

        internal static Uri TokensUri(AppTheme theme)
            => new Uri($"pack://application:,,,/ScreenRecorder;component/Themes/Tokens.{theme}.xaml", UriKind.Absolute);

        /// <summary>
        /// When set, this theme is used instead of the Windows setting (the <c>--theme</c>
        /// command-line switch; handy for testing and screenshots).
        /// </summary>
        public static AppTheme? Override { get; set; }

        /// <summary>
        /// Windows app mode: high contrast wins, then the "Choose your default app mode" setting.
        /// </summary>
        public static AppTheme DetectTheme()
        {
            if (Override.HasValue)
                return Override.Value;

            try
            {
                if (SystemParameters.HighContrast)
                    return AppTheme.HighContrast;
            }
            catch
            {
            }

            return ReadDword(PersonalizeKey, "AppsUseLightTheme", 1) == 0 ? AppTheme.Dark : AppTheme.Light;
        }

        /// <summary>The user's accent color (falls back to the Windows default blue).</summary>
        public static Color DetectAccentColor()
        {
            // AccentColor is stored as 0xAABBGGRR.
            int abgr = ReadDword(DwmKey, "AccentColor", unchecked((int)0xFFD47800));
            return AccentFromAbgr(abgr);
        }

        internal static Color AccentFromAbgr(int abgr)
        {
            byte r = (byte)(abgr & 0xFF);
            byte g = (byte)((abgr >> 8) & 0xFF);
            byte b = (byte)((abgr >> 16) & 0xFF);
            return Color.FromRgb(r, g, b);
        }

        private static int ReadDword(string key, string name, int fallback)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(key);
                return k?.GetValue(name) is int value ? value : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static void ApplyAccent(ResourceDictionary resources, AppTheme theme, Color accent)
        {
            if (theme == AppTheme.HighContrast)
            {
                // High contrast uses the system highlight colors from its token dictionary.
                resources.Remove("AccentFillColorDefaultBrush");
                resources.Remove("AccentFillColorSecondaryBrush");
                resources.Remove("AccentFillColorTertiaryBrush");
                resources.Remove("AccentTextFillColorPrimaryBrush");
                resources.Remove("TextOnAccentFillColorPrimaryBrush");
                return;
            }

            // Fluent: dark themes use a lighter accent shade with dark text on it, light themes a
            // darker shade with white text — both keep text contrast on the accent surface.
            bool dark = theme == AppTheme.Dark;
            Color fill = dark ? Lighten(accent, 0.38) : Darken(accent, 0.12);
            Color text = dark ? Lighten(accent, 0.55) : Darken(accent, 0.30);

            resources["AccentFillColorDefaultBrush"] = Frozen(fill);
            resources["AccentFillColorSecondaryBrush"] = Frozen(WithOpacity(fill, 0.90));
            resources["AccentFillColorTertiaryBrush"] = Frozen(WithOpacity(fill, 0.80));
            resources["AccentTextFillColorPrimaryBrush"] = Frozen(text);
            resources["TextOnAccentFillColorPrimaryBrush"] = Frozen(dark ? Colors.Black : Colors.White);
        }

        internal static Color Lighten(Color c, double amount)
            => Color.FromRgb(Mix(c.R, 255, amount), Mix(c.G, 255, amount), Mix(c.B, 255, amount));

        internal static Color Darken(Color c, double amount)
            => Color.FromRgb(Mix(c.R, 0, amount), Mix(c.G, 0, amount), Mix(c.B, 0, amount));

        internal static Color WithOpacity(Color c, double opacity)
            => Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), c.R, c.G, c.B);

        private static byte Mix(byte from, byte to, double amount)
            => (byte)Math.Round(from + (to - from) * Math.Clamp(amount, 0, 1));

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
