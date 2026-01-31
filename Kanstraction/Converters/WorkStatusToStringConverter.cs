using Kanstraction;
using Kanstraction.Domain.Entities;
using System;
using System.Globalization;
using System.Windows.Data;

namespace Kanstraction.Converters;

public class WorkStatusToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is WorkStatus status)
        {
            var key = status switch
            {
                WorkStatus.NotStarted => "WorkStatus_NotStarted",
                WorkStatus.Ongoing => "WorkStatus_Ongoing",
                WorkStatus.Finished => "WorkStatus_Finished",
                WorkStatus.Paid => "WorkStatus_Paid",
                WorkStatus.Stopped => "WorkStatus_Stopped",
                _ => null
            };

            if (key != null)
            {
                return ResourceHelper.GetString(key, status.ToString());
            }

            return status.ToString();
        }

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
