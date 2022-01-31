using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string parameterString = parameter as string;
            if (parameterString == null)
                return DependencyProperty.UnsetValue;

            if (Enum.IsDefined(value.GetType(), value) == false)
                return DependencyProperty.UnsetValue;

            object parameterValue = Enum.Parse(value.GetType(), parameterString);

            return parameterValue.Equals(value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string parameterString = parameter as string;
            if (parameterString == null)
                return DependencyProperty.UnsetValue;

            return Enum.Parse(targetType, parameterString);
        }
        #endregion
    }

    public class BitrateFormatter : IValueConverter
    {
        #region IValueConverter Members

        #endregion
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                int biteate = System.Convert.ToInt32(value);
                if (biteate >= 1000000)
                {
                    double roundBitrate = biteate / 1000000.0d;
                    if ((Math.Round(roundBitrate, 1) - Math.Floor(roundBitrate)) == 0)
                    {
                        return string.Format("{0:F0}Mbps", Math.Round(roundBitrate, 1));
                    }
                    else
                    {
                        return string.Format("{0:F1}Mbps", Math.Round(roundBitrate, 1));
                    }
                }
                else if (biteate >= 1000)
                {
                    double roundBitrate = biteate / 1000.0d;
                    if ((Math.Round(roundBitrate, 1) - Math.Floor(roundBitrate)) == 0)
                    {
                        return string.Format("{0:F0}Kbps", Math.Round(roundBitrate, 1));
                    }
                    else
                    {
                        return string.Format("{0:F1}Kbps", Math.Round(roundBitrate, 1));
                    }
                }
                else
                {
                    return string.Format("{0}bps", biteate);
                }
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
