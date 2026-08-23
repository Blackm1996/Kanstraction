using Kanstraction.OnSite.Application.Materials.Queries.ListAvailableMaterials;
using System.Windows;
using System.Windows.Controls;

namespace Kanstraction.Views;

public partial class AddMaterialToSubStageDialog : Window
{
    private readonly IReadOnlyList<AvailableMaterialDto> _materials;

    public int? MaterialId { get; private set; }
    public decimal Qty { get; private set; }

    public AddMaterialToSubStageDialog(IReadOnlyList<AvailableMaterialDto> materials)
    {
        InitializeComponent();
        _materials = materials;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CboMaterial.ItemsSource = _materials;
        if (_materials.Count > 0)
            CboMaterial.SelectedIndex = 0;
        TxtQty.Focus();
    }

    private void CboMaterial_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TxtUnit.Text = CboMaterial.SelectedItem is AvailableMaterialDto material
            ? material.Unit
            : string.Empty;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (CboMaterial.SelectedValue is not int materialId)
        {
            MessageBox.Show(
                ResourceHelper.GetString("AddMaterialToSubStageDialog_SelectMaterial", "Select a material."),
                ResourceHelper.GetString("Common_RequiredTitle", "Required"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!decimal.TryParse(TxtQty.Text?.Trim(), out var quantity) || quantity < 0)
        {
            MessageBox.Show(
                ResourceHelper.GetString("AddMaterialToSubStageDialog_InvalidQuantity", "Enter a valid quantity (>= 0)."),
                ResourceHelper.GetString("Common_InvalidTitle", "Invalid"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        MaterialId = materialId;
        Qty = quantity;
        DialogResult = true;
    }
}
