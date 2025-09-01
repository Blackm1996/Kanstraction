using Kanstraction.Data;
using Kanstraction.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Kanstraction.Views
{
    public partial class AdminHubView : UserControl
    {
        private AppDbContext? _db;

        // Caches for quick filtering
        private List<Material> _allMaterials = new();
        private List<StagePreset> _allPresets = new();
        private List<BuildingType> _allBuildingTypes = new();

        public AdminHubView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        public void SetDb(AppDbContext db) => _db = db;

        // -------------------- LIFECYCLE --------------------
        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_db == null) return;

            _allPresets = await _db.StagePresets.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
            _allBuildingTypes = await _db.BuildingTypes.AsNoTracking().OrderBy(b => b.Name).ToListAsync();

            await ReloadMaterialsCacheAsync();
            RefreshMaterialsList();
            RefreshPresetsList();
            RefreshBuildingTypesList();

            // In OnLoaded after _db is set and _allPresets fetched:
            EnsureDesignerHasDb();
            HookDesignerEventsOnce();
            RefreshPresetsList();

            await RefreshPresetSubCountsAsync();

            // Optionally select first preset:
            if (PresetsList.Items.Count > 0 && PresetsList.SelectedIndex < 0)
                PresetsList.SelectedIndex = 0;
        }

        private void AdminTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != AdminTabs) return; // ignore bubbled events

            switch (AdminTabs?.SelectedIndex)
            {
                case 0: // Materials
                    if (MaterialsList != null) RefreshMaterialsList();
                    break;
                case 1: // Stage Presets
                    if (PresetsList != null) RefreshPresetsList();
                    break;
                case 2: // Building Types
                    if (BuildingTypesList != null) ReloadForBuildingTypesTabAsync();
                    break;
            }
        }

        // -------------------- MATERIALS (CRUD) --------------------

        private int? _editingMaterialId = null;   // null => creating new
        private Material? _currentMaterial = null;

        // Fill the list (called on load / search / filter)
        private void RefreshMaterialsList()
        {
            if (MaterialsList == null) return;

            IEnumerable<Material> data = _allMaterials ?? Enumerable.Empty<Material>();

            if (MatActiveOnly?.IsChecked == true)
                data = data.Where(m => m.IsActive);

            var q = MatSearchBox?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(q))
                data = data.Where(m =>
                    (!string.IsNullOrEmpty(m.Name) && m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(m.Unit) && m.Unit.Contains(q, StringComparison.OrdinalIgnoreCase)));

            var list = data.OrderBy(m => m.Name).ToList();
            MaterialsList.ItemsSource = list;

            // keep selection sensible
            if (list.Count > 0 && (MaterialsList.SelectedItem == null || !list.Contains(MaterialsList.SelectedItem)))
                MaterialsList.SelectedIndex = 0;
        }

        private void MatSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshMaterialsList();
        private void MatFilterChanged(object sender, RoutedEventArgs e) => RefreshMaterialsList();

        private async void MaterialsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_db == null) return;
            var m = MaterialsList?.SelectedItem as Material;
            if (m == null)
            {
                ClearMaterialEditor();
                return;
            }

            // Set current editing context
            _editingMaterialId = m.Id;
            _currentMaterial = await _db.Materials.FirstAsync(x => x.Id == m.Id);

            WriteMaterialToEditor(_currentMaterial);

            // Load price history
            var hist = await _db.MaterialPriceHistory
                .Where(h => h.MaterialId == m.Id)
                .OrderByDescending(h => h.StartDate)
                .Select(h => new
                {
                    StartDate = h.StartDate.ToString("dd/MM/yyyy"),
                    EndDate = h.EndDate.HasValue ? h.EndDate.Value.ToString("dd/MM/yyyy") : "",
                    h.PricePerUnit
                })
                .ToListAsync();
            if (MatHistoryGrid != null)
                MatHistoryGrid.ItemsSource = hist;
        }

        private void NewMaterial_Click(object sender, RoutedEventArgs e)
        {
            _editingMaterialId = null;
            _currentMaterial = null;
            ClearMaterialEditor();

            if (MatIsActive != null) MatIsActive.IsChecked = true;

            // deselect list so user knows they're creating a new one
            if (MaterialsList != null) MaterialsList.SelectedItem = null;
        }

        private async void SaveMaterial_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null) return;

            if (!TryReadMaterialFromEditor(out var name, out var unit, out var price, out var effSince, out var isActive, out var validationError))
            {
                MessageBox.Show(validationError, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_editingMaterialId == null)
            {
                // --- Create ---
                var mat = new Material
                {
                    Name = name!,
                    Unit = unit,
                    PricePerUnit = price,
                    EffectiveSince = effSince ?? DateTime.Now,
                    IsActive = isActive
                };
                _db.Materials.Add(mat);
                await _db.SaveChangesAsync();

                // Initial price history row
                _db.MaterialPriceHistory.Add(new MaterialPriceHistory
                {
                    MaterialId = mat.Id,
                    PricePerUnit = price,
                    StartDate = effSince!.Value,
                    EndDate = null
                });
                await _db.SaveChangesAsync();

                _editingMaterialId = mat.Id;
                _currentMaterial = mat;
            }
            else
            {
                // --- Update ---
                var mat = await _db.Materials.FirstAsync(x => x.Id == _editingMaterialId.Value);

                bool priceChanged = mat.PricePerUnit != price;
                bool sinceChanged = mat.EffectiveSince.Date != (effSince ?? DateTime.MinValue).Date;

                mat.Name = name!;
                mat.Unit = unit;
                mat.IsActive = isActive;

                // We always keep current price_per_unit & effective_since in Materials for quick reads
                mat.PricePerUnit = price;
                mat.EffectiveSince = effSince ?? DateTime.Now;

                await _db.SaveChangesAsync();

                // If price or effective date changed, close previous period and open a new one
                if (priceChanged && sinceChanged)
                {
                    await CloseOpenHistoryAndAddNewAsync(mat.Id, price, effSince!.Value);
                }
            }

            // Reload cache + UI and keep selection
            await ReloadMaterialsCacheAsync();
            RefreshMaterialsList();
            if (_editingMaterialId != null && MaterialsList != null)
            {
                var row = _allMaterials.FirstOrDefault(x => x.Id == _editingMaterialId.Value);
                if (row != null) MaterialsList.SelectedItem = row;
            }

            MessageBox.Show("Saved.", "Material", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CancelMaterial_Click(object sender, RoutedEventArgs e)
        {
            // Revert editor to current selection or clear if creating
            if (_editingMaterialId != null && _currentMaterial != null)
                WriteMaterialToEditor(_currentMaterial);
            else
                ClearMaterialEditor();
        }

        // ---------- Helpers ----------

        private void ClearMaterialEditor()
        {
            if (MatName != null) MatName.Text = "";
            if (MatUnit != null) MatUnit.Text = "";
            if (MatPrice != null) MatPrice.Text = "";
            if (MatIsActive != null) MatIsActive.IsChecked = true;
            if (MatHistoryGrid != null) MatHistoryGrid.ItemsSource = null;
        }

        private void WriteMaterialToEditor(Material m)
        {
            if (MatName != null) MatName.Text = m.Name ?? "";
            if (MatUnit != null) MatUnit.Text = m.Unit ?? "";
            if (MatPrice != null) MatPrice.Text = m.PricePerUnit.ToString(CultureInfo.InvariantCulture);
            if (MatIsActive != null) MatIsActive.IsChecked = (m.IsActive);
        }

        private bool TryReadMaterialFromEditor(out string? name, out string? unit, out decimal price,
            out DateTime? effSince, out bool isActive, out string error)
        {
            name = MatName?.Text?.Trim();
            unit = MatUnit?.Text?.Trim();
            isActive = MatIsActive?.IsChecked == true;

            error = "";
            if (string.IsNullOrWhiteSpace(name))
            {
                price = 0; effSince = null;
                error = "Name is required.";
                return false;
            }

            if (!decimal.TryParse(MatPrice?.Text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out price) || price < 0)
            {
                effSince = null;
                error = "Price per unit must be a non-negative number (use dot as decimal separator).";
                return false;
            }

            effSince = DateTime.Today;
            if (effSince == null)
            {
                error = "Effective Since date is required.";
                return false;
            }

            return true;
        }

        private async Task CloseOpenHistoryAndAddNewAsync(int materialId, decimal newPrice, DateTime newStart)
        {
            // Close the currently open history (EndDate null), set EndDate to day before newStart
            var open = await _db!.MaterialPriceHistory
                .Where(h => h.MaterialId == materialId && h.EndDate == null)
                .OrderByDescending(h => h.StartDate)
                .FirstOrDefaultAsync();

            if (open != null)
            {
                var closeDate = newStart.AddDays(-1);
                if (closeDate < open.StartDate) // guard: never invert
                    closeDate = open.StartDate;
                open.EndDate = closeDate;
                await _db.SaveChangesAsync();
            }

            // Add new current period
            _db.MaterialPriceHistory.Add(new MaterialPriceHistory
            {
                MaterialId = materialId,
                PricePerUnit = newPrice,
                StartDate = newStart,
                EndDate = null
            });
            await _db.SaveChangesAsync();
        }

        private async Task ReloadMaterialsCacheAsync()
        {
            _allMaterials = await _db!.Materials.AsNoTracking().OrderBy(m => m.Name).ToListAsync();
        }

        // -------------------- STAGE PRESETS --------------------
        // =======================================================

        // ============ STAGE PRESETS (Master list + Designer host) ============

        private void EnsureDesignerHasDb()
        {
            // Safe to call multiple times; designer can ignore duplicates.
            if (_db != null && PresetDesigner != null)
                PresetDesigner.SetDb(_db);
        }

        private void RefreshPresetsList()
        {
            if (PresetsList == null) return;

            IEnumerable<StagePreset> data = _allPresets ?? Enumerable.Empty<StagePreset>();

            if (PresetActiveOnly?.IsChecked == true)
                data = data.Where(p => p.IsActive);

            var q = PresetSearchBox?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(q))
                data = data.Where(p => !string.IsNullOrEmpty(p.Name) &&
                                       p.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

            var list = data.OrderBy(p => p.Name).ToList();
            PresetsList.ItemsSource = list;

            if (list.Count > 0)
            {
                if (PresetsList.SelectedItem == null || !list.Contains(PresetsList.SelectedItem))
                    PresetsList.SelectedIndex = 0;
            }
            else
            {
                // No items: put designer in Empty State
                EnsureDesignerHasDb();
                PresetDesigner?.EnterEmptyState();
            }
        }

        private void PresetSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshPresetsList();
        private void PresetFilterChanged(object sender, RoutedEventArgs e) => RefreshPresetsList();

        private async void PresetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_db == null || PresetDesigner == null) return;
            EnsureDesignerHasDb();

            var preset = PresetsList?.SelectedItem as StagePreset;
            // If nothing selected (e.g., empty result), load "new preset" surface but don't crash.
            var id = preset?.Id;

            await PresetDesigner.LoadPresetAsync(id);
        }

        private async void NewPreset_Click(object sender, RoutedEventArgs e)
        {
            if (PresetDesigner == null) return;
            EnsureDesignerHasDb();

            // Load the designer in "new" mode (no ID)
            await PresetDesigner.LoadPresetAsync(null);

            // Clear selection in the list to communicate "creating new"
            if (PresetsList != null) PresetsList.SelectedItem = null;
        }

        // (Optional) If your designer raises a Saved event, keep list in sync and select the saved one.
        private void HookDesignerEventsOnce()
        {
            if (PresetDesigner == null) return;

            // Unhook first to avoid multiple subscriptions during reloads
            PresetDesigner.Saved -= PresetDesigner_Saved;
            PresetDesigner.Saved += PresetDesigner_Saved;
        }

        private async void PresetDesigner_Saved(object? sender, int presetId)
        {
            if (_db == null) return;

            // Refresh global presets cache (left list of Stage Presets)
            _allPresets = await _db.StagePresets.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
            RefreshPresetsList();  // keeps Stage Presets tab fresh

            // Also refresh substage counts used by Building Types
            await RefreshPresetSubCountsAsync();

            // If the user is currently on the Building Types tab, refresh its UI too
            if (AdminTabs?.SelectedIndex == 2)  // 0=Materials, 1=Stage Presets, 2=Building Types (adjust if different)
            {
                // Rebuild left list (BT) and picker (available presets)
                _allBuildingTypes = await _db.BuildingTypes.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
                RefreshBuildingTypesList();

                // Recompute counts for current selection and refresh preview
                if (BuildingTypesList?.SelectedItem is BuildingType bt)
                {
                    // simulate selection change to rebuild assigned list + preview
                    BuildingTypesList_SelectionChanged(BuildingTypesList,
                        new SelectionChangedEventArgs(ListBox.SelectionChangedEvent, new List<object>(), new List<object> { bt }));
                }
            }
        }

        // Utility: name copy helper consistent with Materials tab
        private static string SuggestCopyName(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName)) return "Copy";
            const string copy = " (Copy)";
            if (!baseName.EndsWith(copy)) return baseName + copy;
            var i = baseName.LastIndexOf(copy, StringComparison.Ordinal);
            var suffix = baseName[(i + copy.Length)..].Trim();
            if (int.TryParse(suffix, out int n)) return baseName[..i] + copy + " " + (n + 1);
            return baseName + " 2";
        }



        // -------------------- BUILDING TYPES --------------------
        private int? _editingBtId = null;
        private BuildingType? _currentBt = null;
        private ObservableCollection<AssignedPresetVm> _btAssigned = new();
        private Dictionary<int, int> _presetSubCounts = new(); // StagePresetId -> count
        // VM for the assigned presets list
        private class AssignedPresetVm
        {
            public int StagePresetId { get; set; }
            public string Name { get; set; } = "";
            public int OrderIndex { get; set; }
            public int SubStageCount { get; set; }
        }

        private async Task RefreshPresetSubCountsAsync()
        {
            if (_db == null) return;
            _presetSubCounts = await _db.SubStagePresets
                .GroupBy(s => s.StagePresetId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);
        }
        private async Task ReloadForBuildingTypesTabAsync()
        {
            if (_db == null) return;

            // Always refresh presets & their substage counts first (so counts/preview are correct)
            _allPresets = await _db.StagePresets.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
            await RefreshPresetSubCountsAsync();  // fills _presetSubCounts

            // Refresh master BT list and keep selection sensible
            _allBuildingTypes = await _db.BuildingTypes.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
            RefreshBuildingTypesList();

            // If something is selected, rebuild the assigned list + preview
            if (BuildingTypesList?.SelectedItem is BuildingType bt)
            {
                BuildingTypesList_SelectionChanged(BuildingTypesList,
                    new SelectionChangedEventArgs(ListBox.SelectionChangedEvent, new List<object>(), new List<object> { bt }));
            }
            else
            {
                // No selection; clear preview
                if (BtSubStagesPreviewGrid != null) BtSubStagesPreviewGrid.ItemsSource = null;
            }

            // Also refresh the picker (available presets minus assigned), in case it was open
            RefreshBtPresetPicker();
        }
        // Refresh master list (left)
        private void RefreshBuildingTypesList()
        {
            if (BuildingTypesList == null) return;

            IEnumerable<BuildingType> data = _allBuildingTypes ?? Enumerable.Empty<BuildingType>();
            if (BtActiveOnly?.IsChecked == true) data = data.Where(b => b.IsActive);

            var q = BtSearchBox?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(q))
                data = data.Where(b => !string.IsNullOrEmpty(b.Name) &&
                                       b.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

            var list = data.OrderBy(b => b.Name).ToList();
            BuildingTypesList.ItemsSource = list;

            if (list.Count > 0 && (BuildingTypesList.SelectedItem == null || !list.Contains(BuildingTypesList.SelectedItem)))
                BuildingTypesList.SelectedIndex = 0;
        }

        private void BtSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshBuildingTypesList();
        private void BtFilterChanged(object sender, RoutedEventArgs e) => RefreshBuildingTypesList();

        private async void BuildingTypesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_db == null) return;
            var bt = BuildingTypesList?.SelectedItem as BuildingType;
            if (bt == null) return;

            _editingBtId = bt.Id;
            _currentBt = await _db.BuildingTypes.FirstAsync(x => x.Id == bt.Id);

            if (BtName != null) BtName.Text = bt.Name ?? string.Empty;
            if (BtActive != null) BtActive.IsChecked = bt.IsActive;

            // Load assigned presets with sub-stage count
            var assigned = await _db.BuildingTypeStagePresets
                .Where(x => x.BuildingTypeId == bt.Id)
                .Include(x => x.StagePreset)
                .OrderBy(x => x.OrderIndex)
                .Select(x => new { x.StagePresetId, Name = x.StagePreset!.Name, x.OrderIndex })
                .ToListAsync();

            var subCounts = await _db.SubStagePresets
                .GroupBy(s => s.StagePresetId)
                .Select(g => new { StagePresetId = g.Key, Count = g.Count() })
                .ToListAsync();
            var countMap = subCounts.ToDictionary(x => x.StagePresetId, x => x.Count);

            _btAssigned = new ObservableCollection<AssignedPresetVm>(
            assigned.Select(a => new AssignedPresetVm
                {
                    StagePresetId = a.StagePresetId,
                    Name = a.Name,
                    OrderIndex = a.OrderIndex,
                    SubStageCount = _presetSubCounts.TryGetValue(a.StagePresetId, out var c) ? c : 0
                })
            );
            BtAssignedGrid.ItemsSource = _btAssigned;

            // auto-select first assigned preset to show preview
            if (_btAssigned.Count > 0)
            {
                BtAssignedGrid.SelectedIndex = 0;
            }
            else
            {
                BtSubStagesPreviewGrid.ItemsSource = null;
            }

            // fill picker with available (active, not already assigned)
            RefreshBtPresetPicker();
        }

        // New / Duplicate / Archive
        private void NewBuildingType_Click(object sender, RoutedEventArgs e)
        {
            _editingBtId = null;
            _currentBt = null;

            if (BtName != null) BtName.Text = "";
            if (BtActive != null) BtActive.IsChecked = true;

            _btAssigned = new ObservableCollection<AssignedPresetVm>();
            BtAssignedGrid.ItemsSource = _btAssigned;

            RefreshBtPresetPicker();
            BuildingTypesList.SelectedItem = null;
        }

        // Picker shows active presets not yet assigned
        private void RefreshBtPresetPicker()
        {
            if (BtPresetPicker == null) return;

            var assignedIds = _btAssigned.Select(a => a.StagePresetId).ToHashSet();
            var available = (_allPresets ?? new List<StagePreset>())
                .Where(p => p.IsActive && !assignedIds.Contains(p.Id))
                .OrderBy(p => p.Name)
                .ToList();

            BtPresetPicker.ItemsSource = available;
            if (available.Count > 0) BtPresetPicker.SelectedIndex = 0; else BtPresetPicker.SelectedIndex = -1;
        }

        private void AddPresetToBuildingType_Click(object sender, RoutedEventArgs e)
        {
            if (BtPresetPicker?.SelectedValue is not int presetId)
            {
                MessageBox.Show("Select a stage preset to add.");
                return;
            }
            var preset = (_allPresets ?? new List<StagePreset>()).FirstOrDefault(p => p.Id == presetId);
            if (preset == null) return;

            if (_btAssigned.Any(a => a.StagePresetId == presetId)) return;

            var count = _presetSubCounts.TryGetValue(preset.Id, out var c) ? c : 0;

            var vm = new AssignedPresetVm
            {
                StagePresetId = preset.Id,
                Name = preset.Name,
                SubStageCount = count,
                OrderIndex = _btAssigned.Count + 1
            };

            _btAssigned.Add(vm);
            BtAssignedGrid.ItemsSource = _btAssigned;
            RenumberAssigned();
            RefreshBtPresetPicker();

            // ✅ select the newly added preset and show its preview
            BtAssignedGrid.SelectedItem = vm;
            BtAssignedGrid.ScrollIntoView(vm);
        }

        private async void BtAssignedGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_db == null) return;
            var vm = BtAssignedGrid?.SelectedItem as AssignedPresetVm;

            if (vm == null)
            {
                if (BtSelectedPresetTitle != null)
                    BtSelectedPresetTitle.Text = "Select a preset to preview its sub-stages";
                BtSubStagesPreviewGrid.ItemsSource = null;
                return;
            }

            if (BtSelectedPresetTitle != null)
                BtSelectedPresetTitle.Text = vm.Name; // show preset name as the header

            var subs = await _db.SubStagePresets
                .Where(s => s.StagePresetId == vm.StagePresetId)
                .OrderBy(s => s.OrderIndex)
                .Select(s => new { s.OrderIndex, s.Name, s.LaborCost })
                .ToListAsync();

            BtSubStagesPreviewGrid.ItemsSource = subs;
        }

        // Reordering & Remove
        private void BtMoveUp_Click(object sender, RoutedEventArgs e)
        {
            var vm = (sender as FrameworkElement)?.Tag as AssignedPresetVm;
            if (vm == null) return;
            var idx = _btAssigned.IndexOf(vm);
            if (idx <= 0) return;
            _btAssigned.Move(idx, idx - 1);
            RenumberAssigned();
        }

        private void BtMoveDown_Click(object sender, RoutedEventArgs e)
        {
            var vm = (sender as FrameworkElement)?.Tag as AssignedPresetVm;
            if (vm == null) return;
            var idx = _btAssigned.IndexOf(vm);
            if (idx < 0 || idx >= _btAssigned.Count - 1) return;
            _btAssigned.Move(idx, idx + 1);
            RenumberAssigned();
        }

        private void BtRemoveAssigned_Click(object sender, RoutedEventArgs e)
        {
            var vm = (sender as FrameworkElement)?.Tag as AssignedPresetVm;
            if (vm == null) return;
            _btAssigned.Remove(vm);
            RenumberAssigned();
            RefreshBtPresetPicker();
        }

        private void RenumberAssigned()
        {
            for (int i = 0; i < _btAssigned.Count; i++)
                _btAssigned[i].OrderIndex = i + 1;
            BtAssignedGrid.Items.Refresh();
        }

        // Save / Cancel
        private async void SaveBuildingType_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null) return;

            var name = BtName?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Name is required.");
                return;
            }
            var isActive = BtActive?.IsChecked == true;

            using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                BuildingType bt;
                if (_editingBtId == null)
                {
                    bt = new BuildingType { Name = name, IsActive = isActive };
                    _db.BuildingTypes.Add(bt);
                    await _db.SaveChangesAsync();
                    _editingBtId = bt.Id;
                    _currentBt = bt;
                }
                else
                {
                    bt = await _db.BuildingTypes.FirstAsync(x => x.Id == _editingBtId.Value);
                    bt.Name = name;
                    bt.IsActive = isActive;
                    await _db.SaveChangesAsync();
                }

                // Sync join table
                var existingLinks = await _db.BuildingTypeStagePresets
                    .Where(x => x.BuildingTypeId == _editingBtId!.Value)
                    .ToListAsync();

                // delete removed
                var keepIds = _btAssigned.Select(a => a.StagePresetId).ToHashSet();
                var toDelete = existingLinks.Where(x => !keepIds.Contains(x.StagePresetId)).ToList();
                if (toDelete.Count > 0)
                {
                    _db.BuildingTypeStagePresets.RemoveRange(toDelete);
                    await _db.SaveChangesAsync();
                }

                // upsert / set order
                for (int i = 0; i < _btAssigned.Count; i++)
                {
                    var a = _btAssigned[i];
                    var link = existingLinks.FirstOrDefault(x => x.StagePresetId == a.StagePresetId);
                    if (link == null)
                    {
                        link = new BuildingTypeStagePreset
                        {
                            BuildingTypeId = _editingBtId.Value,
                            StagePresetId = a.StagePresetId,
                            OrderIndex = i + 1
                        };
                        _db.BuildingTypeStagePresets.Add(link);
                    }
                    else
                    {
                        link.OrderIndex = i + 1;
                    }
                }
                await _db.SaveChangesAsync();

                await tx.CommitAsync();

                // refresh caches & UI
                _allBuildingTypes = await _db.BuildingTypes.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
                RefreshBuildingTypesList();

                if (_editingBtId != null)
                {
                    var row = _allBuildingTypes.FirstOrDefault(x => x.Id == _editingBtId.Value);
                    if (row != null) BuildingTypesList.SelectedItem = row;
                }

                MessageBox.Show("Building type saved.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                MessageBox.Show("Save failed:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CancelBuildingType_Click(object sender, RoutedEventArgs e)
        {
            // reload current selection
            if (BuildingTypesList?.SelectedItem is BuildingType bt)
            {
                BuildingTypesList_SelectionChanged(BuildingTypesList, new SelectionChangedEventArgs(ListBox.SelectionChangedEvent, new List<object>(), new List<object> { bt }));
            }
            else
            {
                // clear editor
                if (BtName != null) BtName.Text = "";
                if (BtActive != null) BtActive.IsChecked = true;
                _btAssigned = new ObservableCollection<AssignedPresetVm>();
                BtAssignedGrid.ItemsSource = _btAssigned;
                RefreshBtPresetPicker();
                _editingBtId = null;
                _currentBt = null;
            }
        }
    }
    }
