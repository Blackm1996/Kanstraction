using System.Globalization;

namespace Kanstraction;

public static class NumberParsing
{
    public static bool TryParseFlexibleDecimal(string? input, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        const NumberStyles styles = NumberStyles.Number;
        var currentCulture = CultureInfo.CurrentCulture;

        if (decimal.TryParse(trimmed, styles, currentCulture, out value))
        {
            return true;
        }

        if (decimal.TryParse(trimmed, styles, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        var swapped = trimmed.Replace(',', '.');
        if (decimal.TryParse(swapped, styles, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        swapped = trimmed.Replace('.', ',');
        if (decimal.TryParse(swapped, styles, currentCulture, out value))
        {
            return true;
        }

        return false;
    }
}
