using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CustomConverter
{
    /// <summary>
    ///     https://stackoverflow.com/questions/397556/how-to-bind-radiobuttons-to-an-enum
    /// </summary>
    public class EnumBooleanConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var parameterString = parameter as string;
            if (parameterString == null)
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
                var biteate = System.Convert.ToInt32(value);
                if (biteate >= 1000000)
                {
                    var roundBitrate = biteate / 1000000.0d;
                    if (Math.Round(roundBitrate, 1) - Math.Floor(roundBitrate) == 0)
                    {
                        return string.Format("{0:F0}Mbps", Math.Round(roundBitrate, 1));
                    }

                    return string.Format("{0:F1}Mbps", Math.Round(roundBitrate, 1));
                }

                if (biteate >= 1000)
                {
                    var roundBitrate = biteate / 1000.0d;
                    if (Math.Round(roundBitrate, 1) - Math.Floor(roundBitrate) == 0)
                    {
                        return string.Format("{0:F0}Kbps", Math.Round(roundBitrate, 1));
                    }

                    return string.Format("{0:F1}Kbps", Math.Round(roundBitrate, 1));
                }

                return string.Format("{0}bps", biteate);
            }
            catch
            {
                return DependencyProperty.UnsetValue;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #region IValueConverter Members

        #endregion
    }

    public class InvertBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (!(value is bool))
                return DependencyProperty.UnsetValue;
            return !(bool)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Convert(value, targetType, parameter, culture);
        }
    }
}
