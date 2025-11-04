using Kanstraction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Kanstraction.Views;

public partial class ReportDurationDialog : Window
{
    private sealed class DurationPreset
    {
        public int Months { get; init; }
        public string Label { get; init; } = string.Empty;
    }

    private readonly List<DurationPreset> _presets;
    private bool _isInitialized;
    private bool _isApplyingPreset;

    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }

    public ReportDurationDialog()
    {
        InitializeComponent();

        _presets = new List<DurationPreset>
        {
            new() { Months = 1, Label = ResourceHelper.GetString("ProgressReport_PresetOneMonth", "Last 1 month") },
            new() { Months = 3, Label = ResourceHelper.GetString("ProgressReport_PresetThreeMonths", "Last 3 months") },
            new() { Months = 6, Label = ResourceHelper.GetString("ProgressReport_PresetSixMonths", "Last 6 months") },
            new() { Months = 12, Label = ResourceHelper.GetString("ProgressReport_PresetTwelveMonths", "Last 12 months") }
        };

        PresetList.ItemsSource = _presets;
        PresetList.SelectedItem = _presets.First(p => p.Months == 6);

        var today = DateTime.Today;
        EndDatePicker.SelectedDate = today;
        StartDatePicker.SelectedDate = CalculateStartFromPreset(today, 6);

        UpdateMode(isPreset: true);
        _isInitialized = true;
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

    private void UpdateMode(bool isPreset)
    {
        if (PresetList == null || StartDatePicker == null || EndDatePicker == null)
        {
            return;
        }

        PresetList.IsEnabled = isPreset;
        StartDatePicker.IsEnabled = !isPreset;

        if (isPreset)
        {
            ApplySelectedPreset();
        }
    }

    private void ApplySelectedPreset()
    {
        if (!_isInitialized || PresetList.SelectedItem is not DurationPreset preset)
        {
            return;
        }

        if (EndDatePicker.SelectedDate == null)
        {
            EndDatePicker.SelectedDate = DateTime.Today;
        }

        var end = EndDatePicker.SelectedDate ?? DateTime.Today;

        _isApplyingPreset = true;
        StartDatePicker.SelectedDate = CalculateStartFromPreset(end, preset.Months);
        _isApplyingPreset = false;
    }

    private void PresetRadio_Checked(object sender, RoutedEventArgs e)
    {
        UpdateMode(isPreset: true);
    }

    private void CustomRadio_Checked(object sender, RoutedEventArgs e)
    {
        UpdateMode(isPreset: false);
    }

    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetRadio.IsChecked == true)
        {
            ApplySelectedPreset();
        }
    }

    private void EndDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetRadio.IsChecked == true && !_isApplyingPreset)
        {
            ApplySelectedPreset();
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var end = (EndDatePicker.SelectedDate ?? DateTime.Today).Date;
        DateTime start;

        if (PresetRadio.IsChecked == true)
        {
            var preset = PresetList.SelectedItem as DurationPreset ?? _presets.First();
            start = CalculateStartFromPreset(end, preset.Months);
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
