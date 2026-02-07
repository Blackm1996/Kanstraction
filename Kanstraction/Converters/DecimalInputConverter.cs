using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Kanstraction.Converters;

public class DecimalInputConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return value switch
        {
            decimal decimalValue => decimalValue.ToString("0.##", culture),
            decimal? nullableValue => nullableValue?.ToString("0.##", culture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return IsNullable(targetType) ? null : 0m;
        }

        text = text.Trim();
        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
        var alternateSeparator = decimalSeparator == "." ? "," : ".";
        if (text.Contains(alternateSeparator, StringComparison.Ordinal) &&
            !text.Contains(decimalSeparator, StringComparison.Ordinal))
        {
            text = text.Replace(alternateSeparator, decimalSeparator, StringComparison.Ordinal);
        }

        if (decimal.TryParse(text, NumberStyles.Number, culture, out var result) ||
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out result))
        {
            return result;
        }

        return DependencyProperty.UnsetValue;
    }

    private static bool IsNullable(Type targetType) => Nullable.GetUnderlyingType(targetType) != null;
}
