using System;
using System.Globalization;
using System.Windows.Data;
using Kanstraction.Domain.Entities;

namespace Kanstraction.Converters;

public class PaidDateConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            Stage stage => stage.Status == WorkStatus.Paid ? stage.EndDate : null,
            SubStage subStage => subStage.Status == WorkStatus.Paid ? subStage.EndDate : null,
            _ => null
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
