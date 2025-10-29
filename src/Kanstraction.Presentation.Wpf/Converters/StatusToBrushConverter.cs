using Kanstraction.Entities;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Kanstraction.Converters;

public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is WorkStatus s)
        {
            return s switch
            {
                WorkStatus.NotStarted => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0BEC5")),
                WorkStatus.Ongoing => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2196F3")),
                WorkStatus.Finished => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFC107")),
                WorkStatus.Paid => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")),
                WorkStatus.Stopped => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336")),
                _ => Brushes.Gray
            };
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
