using System.Collections.Generic;
﻿using Kanstraction.Data;
using System.Windows;
using Microsoft.EntityFrameworkCore;

using Kanstraction;

namespace Kanstraction.Views;

public partial class AddBuildingDialog : Window
{
    private readonly AppDbContext _db;

    public int? BuildingTypeId { get; private set; }
    public string BuildingCode { get; private set; } = "";

    public AddBuildingDialog(AppDbContext db)
    {
        InitializeComponent();
        _db = db;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Load ACTIVE building types (ordered)
        var types = await _db.BuildingTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync();

        CboType.ItemsSource = types;
        if (types.Count > 0) CboType.SelectedIndex = 0;
        TxtCode.Focus();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var code = TxtCode.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show(
                ResourceHelper.GetString("AddBuildingDialog_CodeRequired", "Enter a building code."),
                ResourceHelper.GetString("Common_RequiredTitle", "Required"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (CboType.SelectedValue is not int typeId)
        {
            MessageBox.Show(
                ResourceHelper.GetString("AddBuildingDialog_TypeRequired", "Select a building type."),
                ResourceHelper.GetString("Common_RequiredTitle", "Required"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Optional (recommended): ensure code is unique within the current project.
        // We can't know the current ProjectId here; OperationsView will validate after dialog returns if needed.
        // If you want this dialog to receive a projectId for validation, pass it in ctor and check here.

        BuildingTypeId = typeId;
        BuildingCode = code;

        // quick sanity check: the chosen type must have at least 1 preset assigned (not required, but nice)
        var assignedStageIds = await _db.BuildingTypeStagePresets
            .Where(x => x.BuildingTypeId == typeId)
            .Select(x => x.StagePresetId)
            .Distinct()
            .ToListAsync();

        if (assignedStageIds.Count == 0)
        {
            var res = MessageBox.Show(
                ResourceHelper.GetString("AddBuildingDialog_NoPresetWarning", "This building type has no stage presets assigned. Create anyway?"),
                ResourceHelper.GetString("AddBuildingDialog_NoPresetTitle", "No preset"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
        }

        var requiredSubStageIds = assignedStageIds.Count == 0
            ? new List<int>()
            : await _db.SubStagePresets
                .Where(s => assignedStageIds.Contains(s.StagePresetId))
                .Select(s => s.Id)
                .ToListAsync();

        bool hasIncompleteDefaults = false;

        if (requiredSubStageIds.Count > 0)
        {
            var laborLookup = await _db.BuildingTypeSubStageLabors
                .Where(x => x.BuildingTypeId == typeId && requiredSubStageIds.Contains(x.SubStagePresetId))
                .ToDictionaryAsync(x => x.SubStagePresetId, x => x.LaborCost);

            if (requiredSubStageIds.Any(id => !laborLookup.TryGetValue(id, out var cost) || cost == null))
            {
                hasIncompleteDefaults = true;
            }

            if (!hasIncompleteDefaults)
            {
                var requiredMaterials = await _db.MaterialUsagesPreset
                    .Where(mu => requiredSubStageIds.Contains(mu.SubStagePresetId))
                    .Select(mu => new { mu.SubStagePresetId, mu.MaterialId })
                    .ToListAsync();

                if (requiredMaterials.Count > 0)
                {
                    var materialLookup = await _db.BuildingTypeMaterialUsages
                        .Where(x => x.BuildingTypeId == typeId && requiredSubStageIds.Contains(x.SubStagePresetId))
                        .ToDictionaryAsync(x => (x.SubStagePresetId, x.MaterialId), x => x.Qty);

                    if (requiredMaterials.Any(req => !materialLookup.TryGetValue((req.SubStagePresetId, req.MaterialId), out var qty) || qty == null))
                    {
                        hasIncompleteDefaults = true;
                    }
                }
            }
        }

        if (hasIncompleteDefaults)
        {
            var res = MessageBox.Show(
                ResourceHelper.GetString("AddBuildingDialog_IncompleteDefaultsWarning", "Some labor costs or material quantities for this building type are missing. Continue anyway?"),
                ResourceHelper.GetString("AddBuildingDialog_IncompleteDefaultsTitle", "Missing defaults"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes)
            {
                return;
            }
        }

        DialogResult = true;
    }
}