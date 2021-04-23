using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Linq;

using FlyleafLib;

namespace FlyleafLib.Controls.WPF
{
    public class QualityToLevelsConverter : IValueConverter
    {
        public enum Qualities
        {
            None,
            Low,
            Med,
            High,
            _4k
        }

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)     
        {
            int videoHeight = (int) value;

            if (videoHeight > 1080)
                return Qualities._4k;
            else if (videoHeight > 720)
                return Qualities.High;
            else if (videoHeight == 720)
                return Qualities.Med;
            else if (videoHeight > 0)
                return Qualities.Low;
            else
                return Qualities.None;
        }
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotImplementedException(); }
	}

    public class VolumeToLevelsConverter : IValueConverter
    {
        public enum Volumes
        {
            Mute,
            Low,
            Med,
            High
        }

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)     
        {
            int volume = (int) value;

            if (volume == 0)
                return Volumes.Mute;
            else if (volume > 99)
                return Volumes.High;
            else if (volume > 49)
                return Volumes.Med;
            else
                return Volumes.Low;
        }
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotImplementedException(); }
	}

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
