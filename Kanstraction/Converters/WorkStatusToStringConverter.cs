using Kanstraction.OnSite.Application.Common;
using System;
using System.Globalization;
using System.Windows.Data;

namespace Kanstraction.Converters;

public sealed class WorkStatusToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not WorkStatusDto status)
        {
            return value?.ToString() ?? string.Empty;
        }

        var resourceKey = status switch
        {
            WorkStatusDto.NotStarted => "WorkStatus_NotStarted",
            WorkStatusDto.Ongoing => "WorkStatus_Ongoing",
            WorkStatusDto.Finished => "WorkStatus_Finished",
            WorkStatusDto.Paid => "WorkStatus_Paid",
            WorkStatusDto.Stopped => "WorkStatus_Stopped",
            _ => null
        };

        return resourceKey is null
            ? status.ToString()
            : ResourceHelper.GetString(resourceKey, status.ToString());
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
