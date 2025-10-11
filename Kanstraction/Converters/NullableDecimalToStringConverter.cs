using System;
using System.Globalization;
using System.Windows.Data;

namespace Kanstraction.Converters;

public class NullableDecimalToStringConverter : IValueConverter
{
    public string? Format { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        decimal? number = value switch
        {
            decimal d => d,
            decimal? nullable => nullable,
            _ => null
        };

        if (!number.HasValue)
        {
            return string.Empty;
        }

        var format = !string.IsNullOrWhiteSpace(Format) ? Format : null;
        return format == null
            ? number.Value.ToString(culture)
            : number.Value.ToString(format, culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text)
        {
            text = text.Trim();
            if (text.Length == 0)
            {
                return null;
            }

            if (decimal.TryParse(text, NumberStyles.Number, culture, out var number))
            {
                return number;
            }

            return Binding.DoNothing;
        }

        if (value == null)
        {
            return null;
        }

        return Binding.DoNothing;
    }
}
