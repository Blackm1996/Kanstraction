using System;
using System.Globalization;
using System.Windows;

using Kanstraction;

namespace Kanstraction.Views
{
    public partial class AddSubStageDialog : Window
    {
        private readonly int _maxOrder;   // = currentCount + 1

        public string SubStageName { get; private set; } = "";
        public decimal LaborCost { get; private set; }
        public int OrderIndex { get; private set; }

        public AddSubStageDialog(int currentSubStagesCount)
        {
            InitializeComponent();
            _maxOrder = Math.Max(1, currentSubStagesCount + 1);

            Loaded += (_, __) =>
            {
                LblOrderHelp.Text = string.Format(
                    ResourceHelper.GetString("AddSubStageDialog_OrderHelpFormat", "Allowed order: 1..{0}"),
                    _maxOrder);
                // Default: add at end
                TxtOrder.Text = _maxOrder.ToString(CultureInfo.InvariantCulture);
                TxtOrder.IsEnabled = false;
                ChkAddAtEnd.IsChecked = true;
                TxtName.Focus();
            };
        }

        private void ChkAddAtEnd_Checked(object sender, RoutedEventArgs e)
        {
            // Lock to end
            TxtOrder.IsEnabled = false;
            TxtOrder.Text = _maxOrder.ToString(CultureInfo.InvariantCulture);
        }

        private void ChkAddAtEnd_Unchecked(object sender, RoutedEventArgs e)
        {
            // Let user type; keep current value but clamp later
        TxtOrder.IsEnabled = true;
        if (!int.TryParse(TxtOrder.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var val) || val < 1)
                TxtOrder.Text = ResourceHelper.GetString("Common_DefaultOrderValue", "1");
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            var name = TxtName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
                MessageBox.Show(
                    ResourceHelper.GetString("AddSubStageDialog_NameRequired", "Please enter a sub-stage name."),
                    ResourceHelper.GetString("Common_RequiredTitle", "Required"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
        }

        if (!decimal.TryParse(TxtLabor.Text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var labor) || labor < 0m)
        {
                MessageBox.Show(
                    ResourceHelper.GetString("AddSubStageDialog_InvalidLabor", "Invalid labor cost."),
                    ResourceHelper.GetString("Common_ValidationTitle", "Validation"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
        }

            int order;
            if (ChkAddAtEnd.IsChecked == true)
            {
                order = _maxOrder; // always append
            }
            else
            {
                if (!int.TryParse(TxtOrder.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out order) || order < 1)
                {
                    MessageBox.Show(
                        ResourceHelper.GetString("AddSubStageDialog_InvalidOrder", "Invalid order index."),
                        ResourceHelper.GetString("Common_ValidationTitle", "Validation"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                // Clamp to 1.._maxOrder
                if (order > _maxOrder) order = _maxOrder;
            }

            SubStageName = name;
            LaborCost = labor;
            OrderIndex = order;

            DialogResult = true;
        }
    }
}