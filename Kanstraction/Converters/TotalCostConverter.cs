using System;
using System.Globalization;
using System.Windows.Data;

namespace Kanstraction.Converters;

public class TotalCostConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 2) return 0m;
        decimal qty = 0m, price = 0m;

        qty = (decimal)values[0];
        price = (decimal)values[1];
       
        var total = qty * price;
        // Return decimal; XAML StringFormat will format it.
        return total;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
