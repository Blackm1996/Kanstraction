using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Kanstraction.Converters;

public class FlexibleDecimalConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            null => string.Empty,
            decimal decimalValue => decimalValue.ToString("0.##", culture),
            decimal? nullableValue => nullableValue.HasValue ? nullableValue.Value.ToString("0.##", culture) : string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text)
        {
            return Binding.DoNothing;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return targetType == typeof(decimal) ? Binding.DoNothing : null;
        }

        return NumberParsing.TryParseFlexibleDecimal(text, out var parsed)
            ? parsed
            : Binding.DoNothing;
    }
}
