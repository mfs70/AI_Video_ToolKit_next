using System;
using System.Globalization;
using System.Windows.Data;

namespace AI_Video_ToolKit_next.UI.Converters;

/// <summary>
/// Преобразует TimeSpan (TotalSeconds) в ширину по коэффициенту масштаба.
/// По умолчанию 1 секунда = 100 пикселей.
/// Параметр конвертера (если задан) используется как коэффициент.
/// </summary>
public class SecondsToWidthConverter : IValueConverter
{
    private const double DefaultPixelsPerSecond = 100.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double seconds = 0;
        if (value is TimeSpan ts)
            seconds = ts.TotalSeconds;
        else if (value is double d)
            seconds = d;

        double pixelsPerSecond = DefaultPixelsPerSecond;
        if (parameter is double p)
            pixelsPerSecond = p;
        else if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var param))
            pixelsPerSecond = param;

        return Math.Max(5, seconds * pixelsPerSecond); // минимум 5 пикселей
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}