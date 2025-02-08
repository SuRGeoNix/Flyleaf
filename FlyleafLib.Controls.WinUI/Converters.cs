using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace FlyleafLib.Controls.WinUI;

public class TicksToTimeSpanConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)     => new TimeSpan((long)value);
	public object ConvertBack(object value, Type targetType, object parameter, string language) => ((TimeSpan)value).Ticks;
}

public class TicksToTimeConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)     => new TimeSpan((long)value).ToString(@"hh\:mm\:ss");
	public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class TicksToSecondsConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)     => (long)value / 10000000.0;

	public object ConvertBack(object value, Type targetType, object parameter, string language) => (long) ((double)value * 10000000);
}

public class TicksToMilliSecondsConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)		=> (int) ((long)value / 10000);

	public object ConvertBack(object value, Type targetType, object parameter, string language) => long.TryParse(value.ToString(), out long res) ? res * 10000 : 0;
}

public class StringToRationalConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)		=> (AspectRatio)value.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, string language) => new AspectRatio(value.ToString());
}

public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)		=>  (bool)value ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
