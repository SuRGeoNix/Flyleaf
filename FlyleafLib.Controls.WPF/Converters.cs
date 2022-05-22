using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

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
            else 
                return Qualities.Low;
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

    public class CheckNullConverter : IMultiValueConverter
    {
		public object Convert(object[] value, Type targetType, object parameter, CultureInfo culture)     
        {
            if (value == null) return true;
            if (value[0] == null || value[0] == System.Windows.DependencyProperty.UnsetValue) return true;
            if (value[1] == null || value[1] == System.Windows.DependencyProperty.UnsetValue) return true;

            return !((IDictionary)value[0]).Contains(value[1]);
        }
		public object[] ConvertBack(object value, Type[] targetType, object parameter, CultureInfo culture) { throw new NotImplementedException(); }
	}

    public class BooleanAllConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            => values.OfType<bool>().All(b => b);

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BooleanConverter<T> : IValueConverter
    {
        public BooleanConverter(T trueValue, T falseValue)
        {
            TrueValue = trueValue;
            FalseValue = falseValue;
        }

        public T TrueValue { get; set; }
        public T FalseValue { get; set; }

        public virtual object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool boolValue && boolValue ? TrueValue : FalseValue;

        public virtual object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is T tValue && System.Collections.Generic.EqualityComparer<T>.Default.Equals(tValue, TrueValue);
    }
    public class InvertBooleanConverter : BooleanConverter<bool>
    {
        public InvertBooleanConverter()
            : base(false, true)
        {
        }
    }

    [ValueConversion(typeof(double), typeof(double), ParameterType = typeof(Orientation))]
    public class SliderValueLabelPositionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is Orientation orientation && value is double width)
            {
                const double halfGripWidth = 9.0;
                const double margin = 4.0;

                switch (orientation)
                {
                    case Orientation.Horizontal:
                        return (-width * 0.5) + halfGripWidth;

                    case Orientation.Vertical:
                        return -width - margin;

                   default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class SumConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            double sum = 0;

            if (values == null) return sum;

            foreach (object value in values)
            {
                if (value == System.Windows.DependencyProperty.UnsetValue) continue;
                sum += (double)value;
            }

            return sum;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class PlaylistItemsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return $"Playlist ({values[0]}/{values[1]})";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class GetDictionaryItemConverter : IMultiValueConverter
    {
		public object Convert(object[] value, Type targetType, object parameter, CultureInfo culture)     
        {
            if (value == null) return null;
            if (value[0] == null || value[0] == System.Windows.DependencyProperty.UnsetValue) return null;
            if (value[1] == null || value[1] == System.Windows.DependencyProperty.UnsetValue) return null;

            return ((IDictionary)value[0])[value[1]];
        }
		public object[] ConvertBack(object value, Type[] targetType, object parameter, CultureInfo culture) { throw new NotImplementedException(); }
	}

    public class MarginConverter : IValueConverter
    {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return new System.Windows.Thickness(0, System.Convert.ToDouble(value), 0, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    [ValueConversion(typeof(Color), typeof(Brush))]
    public class ColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color color)
            {
                return new SolidColorBrush(color);
            }
            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
            {
                return brush.Color;
            }
            return default(Color);
        }
    }

    public class BrushToHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is null) return null;
            string lowerHexString(int i) => i.ToString("X2").ToLower();
            var brush = (SolidColorBrush)value;
            var hex = lowerHexString(brush.Color.R) +
                      lowerHexString(brush.Color.G) +
                      lowerHexString(brush.Color.B);
            return "#" + hex;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ColorToHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is null) return null;

            string lowerHexString(int i) => i.ToString("X2").ToLower();
            Color color = (Color)value;
            var hex = lowerHexString(color.R) +
                      lowerHexString(color.G) +
                      lowerHexString(color.B);
            return hex;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                return ColorConverter.ConvertFromString("#" + value.ToString());
            } catch(Exception) { }

            return Binding.DoNothing;
        }
            
    }
}