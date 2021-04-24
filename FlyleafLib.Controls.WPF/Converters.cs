using System;
using System.Globalization;
using System.Windows.Data;

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
}