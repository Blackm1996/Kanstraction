using Kanstraction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Kanstraction.Views;

public partial class ReportDurationDialog : Window
{
    private sealed class DurationOption
    {
        public int? Months { get; init; }
        public string Label { get; init; } = string.Empty;
        public bool IsCustom => Months == null;
    }

    private readonly List<DurationOption> _options;
    private bool _isInitializing;
    private bool _isApplyingPreset;

    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }

    public ReportDurationDialog()
    {
        InitializeComponent();

        _options = new List<DurationOption>
        {
            new() { Months = 1, Label = ResourceHelper.GetString("ProgressReport_PresetOneMonth", "Last 1 month") },
            new() { Months = 3, Label = ResourceHelper.GetString("ProgressReport_PresetThreeMonths", "Last 3 months") },
            new() { Months = 6, Label = ResourceHelper.GetString("ProgressReport_PresetSixMonths", "Last 6 months") },
            new() { Months = 12, Label = ResourceHelper.GetString("ProgressReport_PresetTwelveMonths", "Last 12 months") },
            new() { Months = null, Label = ResourceHelper.GetString("ProgressReport_CustomLabel", "Custom range") }
        };

        _isInitializing = true;
        DurationCombo.ItemsSource = _options;
        var defaultOption = _options.First(o => o.Months == 6);
        DurationCombo.SelectedItem = defaultOption;

        var today = DateTime.Today;
        EndDatePicker.SelectedDate = today;
        StartDatePicker.SelectedDate = CalculateStartFromPreset(today, defaultOption.Months!.Value);
        ApplySelection(defaultOption);
        _isInitializing = false;
    }

    private static DateTime CalculateStartFromPreset(DateTime endDate, int months)
    {
        var end = endDate.Date;
        var start = end.AddMonths(-months).AddDays(1);
        if (start > end)
        {
            start = end;
        }

        return start;
    }

    private void ApplySelection(DurationOption option)
    {
        if (StartDatePicker == null || EndDatePicker == null)
        {
            return;
        }

        StartDatePicker.IsEnabled = option.IsCustom;

        if (!option.IsCustom)
        {
            var end = (EndDatePicker.SelectedDate ?? DateTime.Today).Date;
            _isApplyingPreset = true;
            StartDatePicker.SelectedDate = CalculateStartFromPreset(end, option.Months!.Value);
            _isApplyingPreset = false;
        }
        else if (StartDatePicker.SelectedDate == null)
        {
            StartDatePicker.SelectedDate = (EndDatePicker.SelectedDate ?? DateTime.Today).Date;
        }
    }

    private void EndDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingPreset)
        {
            return;
        }

        if (DurationCombo.SelectedItem is DurationOption option && !option.IsCustom)
        {
            ApplySelection(option);
        }
    }

    private void DurationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        if (DurationCombo.SelectedItem is DurationOption option)
        {
            ApplySelection(option);
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var end = (EndDatePicker.SelectedDate ?? DateTime.Today).Date;
        DateTime start;

        var option = DurationCombo.SelectedItem as DurationOption ?? _options.First();

        if (!option.IsCustom)
        {
            start = CalculateStartFromPreset(end, option.Months!.Value);
        }
        else
        {
            if (StartDatePicker.SelectedDate == null)
            {
                MessageBox.Show(
                    ResourceHelper.GetString("ProgressReport_InvalidRange", "Start date must be on or before the end date."),
                    ResourceHelper.GetString("Common_InvalidTitle", "Invalid"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            start = StartDatePicker.SelectedDate.Value.Date;
        }

        if (start > end)
        {
            MessageBox.Show(
                ResourceHelper.GetString("ProgressReport_InvalidRange", "Start date must be on or before the end date."),
                ResourceHelper.GetString("Common_InvalidTitle", "Invalid"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        StartDate = start;
        EndDate = end;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
