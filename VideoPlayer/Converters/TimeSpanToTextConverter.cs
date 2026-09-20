using System.Globalization;
using System.Windows.Data;
using VideoPlayer.Helpers;

namespace VideoPlayer.Converters;

public sealed class TimeSpanToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is TimeSpan time ? TimeTextHelper.Format(time) : "00:00:00";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}