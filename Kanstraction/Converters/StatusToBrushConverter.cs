using Kanstraction.OnSite.Application.Common;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Kanstraction.Converters;

public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is WorkStatusDto status)
        {
            return status switch
            {
                WorkStatusDto.NotStarted => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0BEC5")),
                WorkStatusDto.Ongoing => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2196F3")),
                WorkStatusDto.Finished => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFC107")),
                WorkStatusDto.Paid => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")),
                WorkStatusDto.Stopped => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336")),
                _ => Brushes.Gray
            };
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
