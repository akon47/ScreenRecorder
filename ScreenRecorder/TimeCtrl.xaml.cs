using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenRecorder
{
    /// <summary>
    /// Interaction logic for TimeCtrl.xaml
    /// </summary>
    public partial class TimeCtrl : UserControl
    {
        public TimeCtrl()
        {
            InitializeComponent();
        }

        #region Value
        public DateTime Value
        {
            get => (DateTime)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
        public static readonly DependencyProperty ValueProperty =
                DependencyProperty.Register("Value", typeof(DateTime), typeof(TimeCtrl),
                new UIPropertyMetadata(DateTime.Now, new PropertyChangedCallback(OnFullDateTimeChanged)));
        private static void OnFullDateTimeChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            var control = obj as TimeCtrl;
            if (e.NewValue is DateTime newValue)
            {
                control.Day = newValue.Day;
                control.Hour = newValue.Hour;
                control.Minute = newValue.Minute;
                control.Second = newValue.Second;
            }
        }
        #endregion

        #region Day
        public int Day
        {
            get => (int)GetValue(DayProperty);
            set => SetValue(DayProperty, value);
        }
        public static readonly DependencyProperty DayProperty =
                DependencyProperty.Register("Day", typeof(int), typeof(TimeCtrl),
                new UIPropertyMetadata(0, new PropertyChangedCallback(OnPartOfDateTimeChanged)));
        #endregion

        #region Hour
        public int Hour
        {
            get => (int)GetValue(HourProperty);
            set => SetValue(HourProperty, value);
        }
        public static readonly DependencyProperty HourProperty =
                DependencyProperty.Register("Hour", typeof(int), typeof(TimeCtrl),
                new UIPropertyMetadata(0, new PropertyChangedCallback(OnPartOfDateTimeChanged)));
        #endregion

        #region Minute
        public int Minute
        {
            get => (int)GetValue(MinuteProperty);
            set => SetValue(MinuteProperty, value);
        }
        public static readonly DependencyProperty MinuteProperty =
                DependencyProperty.Register("Minute", typeof(int), typeof(TimeCtrl),
                new UIPropertyMetadata(0, new PropertyChangedCallback(OnPartOfDateTimeChanged)));
        #endregion

        #region Second
        public int Second
        {
            get => (int)GetValue(SecondProperty);
            set => SetValue(SecondProperty, value);
        }
        public static readonly DependencyProperty SecondProperty =
                DependencyProperty.Register("Second", typeof(int), typeof(TimeCtrl),
                new UIPropertyMetadata(0, new PropertyChangedCallback(OnPartOfDateTimeChanged)));
        #endregion

        private static void OnPartOfDateTimeChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            if (obj is TimeCtrl control)
            {
                // Synchronize DateTime value with changed day, hour, minute or second value
                DateTime fullDateTime = control.Value;
                control.Value = new DateTime(fullDateTime.Year, fullDateTime.Month, control.Day, control.Hour, control.Minute, control.Second);
            }
        }

        private void OnKey(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Up) 
                Spin(((Grid)sender).Name, 1);
            if (args.Key == Key.Down)
                Spin(((Grid)sender).Name, -1);
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            Spin(((Grid)sender).Name, e.Delta);
        }

        private void Spin(string senderName, int delta)
        {
            // spin with turn around
            switch (senderName)
            {
                case "day":
                    var now = DateTime.Now;
                    int nDaysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
                    int nDay = Day;
                    if (delta > 0)
                        nDay = nDay < nDaysInMonth ? nDay + 1 : 1;
                    if (delta < 0)
                        nDay = nDay > 1 ? nDay - 1 : nDaysInMonth;
                    Day = nDay;
                    break;
                case "hour":
                    int nHour = Hour;
                    if (delta > 0)
                        nHour = (nHour + 1) % 24;
                    if (delta < 0)
                        nHour = nHour > 0 ? nHour - 1 : 23;
                    Hour = nHour;
                    break;
                case "min":
                    int nMinute = Minute;
                    if (delta > 0)
                        nMinute = (nMinute + 1) % 60;
                    if (delta < 0)
                        nMinute = nMinute > 0 ? nMinute - 1 : 59;
                    Minute = nMinute;
                    break;
                case "sec":
                    int nSecond = Second;
                    if (delta > 0)
                        nSecond = (nSecond + 1) % 60;
                    if (delta < 0)
                        nSecond = nSecond > 0 ? nSecond - 1 : 59;
                    Second = nSecond;
                    break;
            }
        }
    }
}
