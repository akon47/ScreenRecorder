using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CustomConverter
{
    /// <summary>
    /// https://stackoverflow.com/questions/397556/how-to-bind-radiobuttons-to-an-enum
    /// </summary>
    public class EnumBooleanConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var parameterString = parameter as string;
            if (parameterString == null || value == null)
            {
                return DependencyProperty.UnsetValue;
            }

            if (Enum.IsDefined(value.GetType(), value) == false)
            {
                return DependencyProperty.UnsetValue;
            }

            var parameterValue = Enum.Parse(value.GetType(), parameterString);

            return parameterValue.Equals(value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var parameterString = parameter as string;
            if (parameterString == null)
            {
                return DependencyProperty.UnsetValue;
            }

            // A radio button that is being UNchecked must not write its value back.
            if (value is bool isChecked && !isChecked)
            {
                return Binding.DoNothing;
            }

            return Enum.Parse(targetType, parameterString);
        }

        #endregion
    }

    public class BitrateFormatter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                return Format(System.Convert.ToInt32(value));
            }
            catch
            {
                return DependencyProperty.UnsetValue;
            }
        }

        /// <summary>"5Mbps", "1.5Mbps", "160Kbps", "800bps" — one decimal only when needed.</summary>
        public static string Format(int bitrate)
        {
            if (bitrate >= 1000000)
            {
                return FormatScaled(bitrate / 1000000.0d, "Mbps");
            }

            if (bitrate >= 1000)
            {
                return FormatScaled(bitrate / 1000.0d, "Kbps");
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}bps", bitrate);
        }

        private static string FormatScaled(double value, string unit)
        {
            double rounded = Math.Round(value, 1);
            return rounded - Math.Floor(rounded) == 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:F0}{1}", rounded, unit)
                : string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}", rounded, unit);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class TrueToParameter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value as bool? != true)
                return DependencyProperty.UnsetValue;

            return parameter;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>Frame-rate list item: 0 is the automatic (changes-only) mode, anything else "N fps".</summary>
    public class FrameRateFormatter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                return Format(System.Convert.ToInt32(value), ScreenRecorder.Properties.Resources.FrameRateAuto);
            }
            catch
            {
                return DependencyProperty.UnsetValue;
            }
        }

        public static string Format(int frameRate, string autoText)
        {
            return frameRate <= 0 ? autoText : string.Format(CultureInfo.InvariantCulture, "{0} fps", frameRate);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>Start-delay list item: 0 is "None", anything else "N sec".</summary>
    public class RecordDelayFormatter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                return Format(System.Convert.ToInt32(value),
                    ScreenRecorder.Properties.Resources.RecordDelayNone,
                    ScreenRecorder.Properties.Resources.SecondsFormat);
            }
            catch
            {
                return DependencyProperty.UnsetValue;
            }
        }

        public static string Format(int seconds, string noneText, string secondsFormat)
        {
            return seconds <= 0 ? noneText : string.Format(CultureInfo.CurrentCulture, secondsFormat, seconds);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>Visible when false, Collapsed when true.</summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>True for a non-null value (and, for strings, a non-empty one).</summary>
    public class NotNullConverter : IValueConverter
    {
        public static readonly NotNullConverter Instance = new NotNullConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
                return text.Length > 0;

            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Elapsed recording time as shown on the toolbar: <c>hh:mm:ss</c>. Whole seconds only, so
    /// the text changes once a second (no per-frame string churn, and no constantly changing
    /// pixels on screen while recording).
    /// </summary>
    public static class TimeFormatter
    {
        public static string Format(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;

            long total = (long)elapsed.TotalSeconds;
            return FormatSeconds(total);
        }

        public static string FormatSeconds(long totalSeconds)
        {
            if (totalSeconds < 0)
                totalSeconds = 0;

            long hours = totalSeconds / 3600;
            long minutes = totalSeconds % 3600 / 60;
            long seconds = totalSeconds % 60;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", hours, minutes, seconds);
        }

        /// <summary>"12.3 MB", "1.25 GB", "640 KB".</summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 0)
                bytes = 0;

            const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
            if (bytes >= GB)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.00} GB", bytes / GB);
            if (bytes >= MB)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", bytes / MB);
            if (bytes >= KB)
                return string.Format(CultureInfo.InvariantCulture, "{0:0} KB", bytes / KB);
            return string.Format(CultureInfo.InvariantCulture, "{0} B", bytes);
        }
    }
}
