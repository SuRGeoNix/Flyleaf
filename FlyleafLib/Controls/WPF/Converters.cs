using System;
using System.Globalization;
using System.Windows.Data;

namespace FlyleafLib.Controls.WPF
{
    public class TicksToTimeConverter : IValueConverter
    {
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)     { return (string) new TimeSpan((long)value).ToString(@"hh\:mm\:ss"); }
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotImplementedException(); }
	}

    public class TicksToSecondsConverter : IValueConverter
    {
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)     { return (double) ((long)value / 10000000.0); }

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { return (long) ((double)value * (long)10000000); }
	}

    public class TicksToMilliSecondsConverter : IValueConverter
    {
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)     { return (int) ((long)value / 10000); }

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { return long.Parse(value.ToString()) * 10000; }
	}

    public class StringToRationalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return value.ToString(); }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { return new AspectRatio(value.ToString()); }
    }
}