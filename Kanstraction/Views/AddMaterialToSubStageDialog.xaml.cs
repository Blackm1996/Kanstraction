using Kanstraction.Infrastructure.Data;
using Kanstraction.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using Kanstraction;

namespace Kanstraction.Views;

/// <summary>
/// Interaction logic for AddMaterialToSubStageDialog.xaml
/// </summary>
public partial class AddMaterialToSubStageDialog : Window
{
    private readonly AppDbContext _db;
    private readonly int _subStageId;

    public int? MaterialId { get; private set; }
    public decimal Qty { get; private set; }

    public AddMaterialToSubStageDialog(AppDbContext db, int subStageId)
    {
        InitializeComponent();
        _db = db;
        _subStageId = subStageId;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Load active materials that are NOT already used by this sub-stage
        var usedIds = await _db.MaterialUsages
            .Where(mu => mu.SubStageId == _subStageId)
            .Select(mu => mu.MaterialId)
            .ToListAsync();

        var materials = await _db.Materials
            .AsNoTracking()
            .Include(m => m.MaterialCategory)
            .Where(m => m.IsActive && !usedIds.Contains(m.Id))
            .OrderBy(m => m.MaterialCategory != null ? m.MaterialCategory.Name : string.Empty)
            .ThenBy(m => m.Name)
            .ToListAsync();

        CboMaterial.ItemsSource = materials;
        if (materials.Count > 0) CboMaterial.SelectedIndex = 0;
        TxtQty.Focus();
    }

    private void CboMaterial_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboMaterial.SelectedItem is Material m)
            TxtUnit.Text = m.Unit ?? "";
        else
            TxtUnit.Text = "";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (CboMaterial.SelectedValue is not int matId)
        {
            MessageBox.Show(
                ResourceHelper.GetString("AddMaterialToSubStageDialog_SelectMaterial", "Select a material."),
                ResourceHelper.GetString("Common_RequiredTitle", "Required"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (!decimal.TryParse(TxtQty.Text?.Trim(), out var qty) || qty < 0)
        {
            MessageBox.Show(
                ResourceHelper.GetString("AddMaterialToSubStageDialog_InvalidQuantity", "Enter a valid quantity (>= 0)."),
                ResourceHelper.GetString("Common_InvalidTitle", "Invalid"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        MaterialId = matId;
        Qty = qty;
        DialogResult = true;
    }
}
