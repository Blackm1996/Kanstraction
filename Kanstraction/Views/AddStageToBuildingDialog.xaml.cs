using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

using Kanstraction;

namespace Kanstraction.Views;

public partial class AddStageToBuildingDialog : Window
{
    private readonly int _maxOrder;

    public int? SelectedPresetId { get; private set; }
    public int OrderIndex { get; private set; }

    public sealed class PresetVm
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public AddStageToBuildingDialog(IEnumerable<PresetVm> presets, int currentStageCount)
    {
        InitializeComponent();

        _maxOrder = Math.Max(1, currentStageCount + 1);
        CboPreset.ItemsSource = presets;

        Loaded += (_, __) =>
        {
            LblOrderHelp.Text = string.Format(
                ResourceHelper.GetString("AddStageToBuildingDialog_OrderHelpFormat", "Allowed order: 1..{0}"),
                _maxOrder);
            TxtOrder.Text = _maxOrder.ToString(CultureInfo.InvariantCulture);
            TxtOrder.IsEnabled = false;
            ChkAddAtEnd.IsChecked = true;

            // pick first available by default
            if (CboPreset.Items.Count > 0)
                CboPreset.SelectedIndex = 0;
        };
    }

    private void ChkAddAtEnd_Checked(object sender, RoutedEventArgs e)
    {
        TxtOrder.IsEnabled = false;
        TxtOrder.Text = _maxOrder.ToString(CultureInfo.InvariantCulture);
    }

    private void ChkAddAtEnd_Unchecked(object sender, RoutedEventArgs e)
    {
        TxtOrder.IsEnabled = true;
        if (!int.TryParse(TxtOrder.Text?.Trim(), out var val) || val < 1)
            TxtOrder.Text = ResourceHelper.GetString("Common_DefaultOrderValue", "1");
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (CboPreset.SelectedValue == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("AddStageToBuildingDialog_SelectPresetMessage", "Please choose a stage preset."),
                ResourceHelper.GetString("Common_RequiredTitle", "Required"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        int order;
        if (ChkAddAtEnd.IsChecked == true)
        {
            order = _maxOrder;
        }
        else
        {
            if (!int.TryParse(TxtOrder.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out order) || order < 1)
            {
                MessageBox.Show(
                    ResourceHelper.GetString("AddStageToBuildingDialog_InvalidOrderMessage", "Invalid order index."),
                    ResourceHelper.GetString("Common_ValidationTitle", "Validation"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            if (order > _maxOrder) order = _maxOrder;
        }

        SelectedPresetId = (int)CboPreset.SelectedValue;
        OrderIndex = order;

        DialogResult = true;
    }
}
