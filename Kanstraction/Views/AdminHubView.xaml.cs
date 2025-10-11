using Kanstraction;
using Kanstraction.Data;
using Kanstraction.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Kanstraction.Views
{
    public partial class AdminHubView : UserControl
    {
        private AppDbContext? _db;

        // Caches for quick filtering
        private List<Material> _allMaterials = new();
        private List<MaterialCategory> _allMaterialCategories = new();
        private List<StagePreset> _allPresets = new();
        private List<BuildingType> _allBuildingTypes = new();

        public bool IsMaterialDirty
        {
            get => (bool)GetValue(IsMaterialDirtyProperty);
            set => SetValue(IsMaterialDirtyProperty, value);
        }

        public static readonly DependencyProperty IsMaterialDirtyProperty =
            DependencyProperty.Register(nameof(IsMaterialDirty), typeof(bool), typeof(AdminHubView), new PropertyMetadata(false));

        public bool IsBuildingDirty
        {
            get => (bool)GetValue(IsBuildingDirtyProperty);
            set => SetValue(IsBuildingDirtyProperty, value);
        }

        public static readonly DependencyProperty IsBuildingDirtyProperty =
            DependencyProperty.Register(nameof(IsBuildingDirty), typeof(bool), typeof(AdminHubView), new PropertyMetadata(false));

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

            if (MatCategoryFilter?.SelectedValue is int categoryId)
                data = data.Where(m => m.MaterialCategoryId == categoryId);

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

        private void MaterialEditor_TextChanged(object sender, TextChangedEventArgs e) => UpdateMaterialDirtyState();
        private void MaterialEditor_CheckChanged(object sender, RoutedEventArgs e) => UpdateMaterialDirtyState();
        private void MatCategory_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateMaterialDirtyState();

        private async void AddCategory_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null) return;

            var dialog = new PromptTextDialog(ResourceHelper.GetString("AdminHubView_NewCategoryDialogTitle", "New category"));
            var owner = Window.GetWindow(this);
            if (owner != null)
            {
                dialog.Owner = owner;
            }

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value))
            {
                return;
            }

            var name = dialog.Value.Trim();

            if (_allMaterialCategories.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    ResourceHelper.GetString("AdminHubView_CategoryExistsMessage", "A category with this name already exists."),
                    ResourceHelper.GetString("Common_InvalidTitle", "Invalid"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var category = new MaterialCategory
            {
                Name = name
            };

            _db.MaterialCategories.Add(category);
            await _db.SaveChangesAsync();

            await ReloadMaterialsCacheAsync();
            RefreshMaterialsList();

            if (MatCategory != null)
            {
                MatCategory.SelectedValue = category.Id;
            }
        }

        private async void MaterialsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_db == null) return;
            var m = MaterialsList?.SelectedItem as Material;
            if (m == null)
            {
                _editingMaterialId = null;
                _currentMaterial = null;
                ClearMaterialEditor();
                UpdateMaterialDirtyState();
                return;
            }

            // Set current editing context
            _editingMaterialId = m.Id;
            _currentMaterial = await _db.Materials
                .Include(x => x.MaterialCategory)
                .FirstAsync(x => x.Id == m.Id);

            WriteMaterialToEditor(_currentMaterial);
            UpdateMaterialDirtyState();

            // Load price history
            var historyRows = await _db.MaterialPriceHistory
                .Where(h => h.MaterialId == m.Id)
                .OrderByDescending(h => h.StartDate)
                .Select(h => new { h.StartDate, h.EndDate, h.PricePerUnit })
                .ToListAsync();

            var dateFormat = ResourceHelper.GetString("Common_DateFormat", "dd/MM/yyyy");
            var hist = historyRows.Select(h => new
            {
                StartDate = h.StartDate.ToString(dateFormat),
                EndDate = h.EndDate.HasValue ? h.EndDate.Value.ToString(dateFormat) : string.Empty,
                h.PricePerUnit
            }).ToList();

            if (MatHistoryGrid != null)
                MatHistoryGrid.ItemsSource = hist;
        }

        private void NewMaterial_Click(object sender, RoutedEventArgs e)
        {
            _editingMaterialId = null;
            _currentMaterial = null;
            ClearMaterialEditor();

            if (MatIsActive != null) MatIsActive.IsChecked = true;
            if (MatCategory != null) MatCategory.SelectedIndex = -1;

            // deselect list so user knows they're creating a new one
            if (MaterialsList != null) MaterialsList.SelectedItem = null;
        }

        private async void SaveMaterial_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null) return;

            if (!TryReadMaterialFromEditor(out var name, out var unit, out var price, out var effSince, out var isActive, out var categoryId, out var validationError))
            {
                MessageBox.Show(validationError,
                    ResourceHelper.GetString("Common_ValidationTitle", "Validation"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    IsActive = isActive,
                    MaterialCategoryId = categoryId
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
                mat.MaterialCategoryId = categoryId;

                // We always keep current price_per_unit & effective_since in Materials for quick reads
                mat.PricePerUnit = price;
                mat.EffectiveSince = effSince ?? DateTime.Now;

                await _db.SaveChangesAsync();

                // If price changed, maintain history depending on whether the effective date shifted
                if (priceChanged)
                {
                    if (sinceChanged)
                    {
                        await CloseOpenHistoryAndAddNewAsync(mat.Id, price, effSince!.Value);
                    }
                    else
                    {
                        var openHistory = await _db!.MaterialPriceHistory
                            .Where(h => h.MaterialId == mat.Id && h.EndDate == null)
                            .OrderByDescending(h => h.StartDate)
                            .FirstOrDefaultAsync();

                        if (openHistory != null)
                        {
                            openHistory.PricePerUnit = price;
                            await _db.SaveChangesAsync();
                        }
                        else
                        {
                            await CloseOpenHistoryAndAddNewAsync(mat.Id, price, effSince!.Value);
                        }
                    }

                    await UpdateUsageDatesForActiveSubStagesAsync(mat.Id, (effSince ?? DateTime.Today).Date);
                }
            }

            // Reload cache + UI and keep selection
            var savedMaterialId = _editingMaterialId;

            await ReloadMaterialsCacheAsync();
            RefreshMaterialsList();

            if (savedMaterialId != null)
            {
                SelectMaterialInList(savedMaterialId.Value);
                Dispatcher.BeginInvoke(new Action(() => SelectMaterialInList(savedMaterialId.Value)), DispatcherPriority.ContextIdle);
            }

            MessageBox.Show(
                ResourceHelper.GetString("AdminHubView_MaterialSavedMessage", "Saved."),
                ResourceHelper.GetString("AdminHubView_MaterialDialogTitle", "Material"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void SelectMaterialInList(int materialId)
        {
            if (MaterialsList == null) return;

            foreach (var item in MaterialsList.Items)
            {
                if (item is Material material && material.Id == materialId)
                {
                    MaterialsList.SelectedItem = item;
                    MaterialsList.ScrollIntoView(item);
                    break;
                }
            }
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
            if (MatCategory != null) MatCategory.SelectedIndex = -1;
            if (MatHistoryGrid != null) MatHistoryGrid.ItemsSource = null;
            UpdateMaterialDirtyState();
        }

        private void WriteMaterialToEditor(Material m)
        {
            if (MatName != null) MatName.Text = m.Name ?? "";
            if (MatUnit != null) MatUnit.Text = m.Unit ?? "";
            if (MatPrice != null) MatPrice.Text = m.PricePerUnit.ToString(CultureInfo.InvariantCulture);
            if (MatIsActive != null) MatIsActive.IsChecked = (m.IsActive);
            if (MatCategory != null)
            {
                if (_allMaterialCategories.Any(c => c.Id == m.MaterialCategoryId))
                    MatCategory.SelectedValue = m.MaterialCategoryId;
                else
                    MatCategory.SelectedIndex = -1;
            }
            UpdateMaterialDirtyState();
        }

        private bool TryReadMaterialFromEditor(out string? name, out string? unit, out decimal price,
            out DateTime? effSince, out bool isActive, out int categoryId, out string error)
        {
            name = MatName?.Text?.Trim();
            unit = MatUnit?.Text?.Trim();
            isActive = MatIsActive?.IsChecked == true;
            categoryId = 0;

            error = "";
            if (string.IsNullOrWhiteSpace(name))
            {
                price = 0; effSince = null;
                error = ResourceHelper.GetString("AdminHubView_MaterialNameRequired", "Name is required.");
                return false;
            }

            if (MatCategory?.SelectedValue is not int selectedCategoryId)
            {
                price = 0; effSince = null;
                error = ResourceHelper.GetString("AdminHubView_CategoryRequired", "Category is required.");
                return false;
            }

            categoryId = selectedCategoryId;

            if (!decimal.TryParse(MatPrice?.Text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out price) || price < 0)
            {
                effSince = null;
                error = ResourceHelper.GetString("AdminHubView_MaterialPriceInvalid", "Price per unit must be a non-negative number (use dot as decimal separator).");
                return false;
            }

            effSince = DateTime.Today;
            if (effSince == null)
            {
                error = ResourceHelper.GetString("AdminHubView_EffectiveSinceRequired", "Effective Since date is required.");
                return false;
            }

            return true;
        }

        private void UpdateMaterialDirtyState()
        {
            if (MatName == null || MatUnit == null || MatPrice == null || MatIsActive == null)
            {
                IsMaterialDirty = false;
                return;
            }

            var name = MatName.Text?.Trim() ?? string.Empty;
            var unit = MatUnit.Text?.Trim() ?? string.Empty;
            var priceText = MatPrice.Text?.Trim() ?? string.Empty;
            var isActive = MatIsActive.IsChecked == true;
            int? selectedCategoryId = MatCategory?.SelectedValue switch
            {
                int value => value,
                _ => null
            };

            bool dirty;

            if (_currentMaterial == null)
            {
                dirty = !(string.IsNullOrEmpty(name) &&
                          string.IsNullOrEmpty(unit) &&
                          string.IsNullOrEmpty(priceText) &&
                          isActive &&
                          !selectedCategoryId.HasValue);
            }
            else
            {
                dirty = !string.Equals(name, _currentMaterial.Name ?? string.Empty, StringComparison.Ordinal) ||
                        !string.Equals(unit, _currentMaterial.Unit ?? string.Empty, StringComparison.Ordinal) ||
                        isActive != _currentMaterial.IsActive ||
                        selectedCategoryId != _currentMaterial.MaterialCategoryId;

                if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
                {
                    dirty = true;
                }
                else if (!dirty)
                {
                    dirty = price != _currentMaterial.PricePerUnit;
                }
                else if (price != _currentMaterial.PricePerUnit)
                {
                    dirty = true;
                }
            }

            IsMaterialDirty = dirty;
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

        private async Task UpdateUsageDatesForActiveSubStagesAsync(int materialId, DateTime newUsageDate)
        {
            var activeUsages = await _db!.MaterialUsages
                .Where(mu => mu.MaterialId == materialId &&
                            mu.SubStage.Status != WorkStatus.Finished &&
                            mu.SubStage.Status != WorkStatus.Paid &&
                            mu.SubStage.Status != WorkStatus.Stopped)
                .ToListAsync();

            if (activeUsages.Count == 0)
                return;

            foreach (var usage in activeUsages)
            {
                usage.UsageDate = newUsageDate;
            }

            await _db.SaveChangesAsync();
        }

        private sealed class CategoryOption
        {
            public int? Id { get; init; }
            public string Name { get; init; } = string.Empty;
        }

        private async Task ReloadMaterialsCacheAsync()
        {
            _allMaterialCategories = await _db!.MaterialCategories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();

            _allMaterials = await _db!.Materials
                .AsNoTracking()
                .Include(m => m.MaterialCategory)
                .OrderBy(m => m.Name)
                .ToListAsync();

            RefreshMaterialCategoryControls();
        }

        private void RefreshMaterialCategoryControls()
        {
            if (MatCategory != null)
            {
                int? previousCategoryId = MatCategory.SelectedValue switch
                {
                    int value => value,
                    _ => null
                };

                MatCategory.ItemsSource = _allMaterialCategories;

                if (previousCategoryId.HasValue && _allMaterialCategories.Any(c => c.Id == previousCategoryId.Value))
                {
                    MatCategory.SelectedValue = previousCategoryId.Value;
                }
                else if (_currentMaterial != null && _allMaterialCategories.Any(c => c.Id == _currentMaterial.MaterialCategoryId))
                {
                    MatCategory.SelectedValue = _currentMaterial.MaterialCategoryId;
                }
                else
                {
                    MatCategory.SelectedIndex = -1;
                }
            }

            if (MatCategoryFilter != null)
            {
                int? previousFilterId = MatCategoryFilter.SelectedValue switch
                {
                    int value => value,
                    _ => null
                };

                var items = new List<CategoryOption>
                {
                    new CategoryOption
                    {
                        Id = null,
                        Name = ResourceHelper.GetString("AdminHubView_CategoryFilterAll", "All categories")
                    }
                };

                items.AddRange(_allMaterialCategories
                    .Select(c => new CategoryOption
                    {
                        Id = c.Id,
                        Name = c.Name ?? string.Empty
                    }));

                MatCategoryFilter.ItemsSource = items;

                if (previousFilterId.HasValue && items.Any(i => i.Id == previousFilterId.Value))
                {
                    MatCategoryFilter.SelectedValue = previousFilterId.Value;
                }
                else
                {
                    MatCategoryFilter.SelectedIndex = 0;
                }
            }
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
            var copyLabel = ResourceHelper.GetString("AdminHubView_CopyBaseName", "Copy");
            var copySuffix = ResourceHelper.GetString("AdminHubView_CopySuffix", " (Copy)");

            if (string.IsNullOrWhiteSpace(baseName)) return copyLabel;
            if (!baseName.EndsWith(copySuffix, StringComparison.Ordinal)) return baseName + copySuffix;

            var i = baseName.LastIndexOf(copySuffix, StringComparison.Ordinal);
            var suffix = baseName[(i + copySuffix.Length)..].Trim();
            if (int.TryParse(suffix, out int n))
                return string.Format(CultureInfo.InvariantCulture, "{0}{1} {2}", baseName[..i], copySuffix, n + 1);

            return string.Format(
                ResourceHelper.GetString("AdminHubView_CopyDefaultFormat", "{0} 2"),
                baseName);
        }



        // -------------------- BUILDING TYPES --------------------
        private int? _editingBtId = null;
        private BuildingType? _currentBt = null;
        private ObservableCollection<AssignedPresetVm> _btAssigned = new();
        private List<int> _currentBtAssignedIds = new();
        private Dictionary<int, int> _presetSubCounts = new(); // StagePresetId -> count
        private Dictionary<int, ObservableCollection<SubStageLaborVm>> _btSubStageLabors = new();
        private Dictionary<int, decimal> _currentBtLaborMap = new();
        private Dictionary<int, decimal> _currentBtMaterialMap = new();
        private SubStageLaborVm? _selectedSubStageVm = null;
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
                if (BtSubStagesGrid != null) BtSubStagesGrid.ItemsSource = null;
                UpdateMaterialsPanel(null);
                _btSubStageLabors = new Dictionary<int, ObservableCollection<SubStageLaborVm>>();
                _currentBtLaborMap = new Dictionary<int, decimal>();
                _currentBtMaterialMap = new Dictionary<int, decimal>();
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

        private void BuildingEditor_TextChanged(object sender, TextChangedEventArgs e) => UpdateBuildingDirtyState();
        private void BuildingEditor_CheckChanged(object sender, RoutedEventArgs e) => UpdateBuildingDirtyState();

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
            _currentBtAssignedIds = _btAssigned.Select(a => a.StagePresetId).ToList();

            await LoadSubStageLaborsForAssignedPresetsAsync(bt.Id);

            // auto-select first assigned preset to show preview
            if (_btAssigned.Count > 0)
            {
                BtAssignedGrid.SelectedIndex = 0;
            }
            else
            {
                if (BtSubStagesGrid != null) BtSubStagesGrid.ItemsSource = null;
                UpdateMaterialsPanel(null);
            }

            // fill picker with available (active, not already assigned)
            RefreshBtPresetPicker();
            UpdateBuildingDirtyState();
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
            _currentBtAssignedIds = new List<int>();
            foreach (var collection in _btSubStageLabors.Values)
            {
                foreach (var vm in collection)
                    vm.PropertyChanged -= SubStageLaborVm_PropertyChanged;
            }
            _btSubStageLabors = new Dictionary<int, ObservableCollection<SubStageLaborVm>>();
            _currentBtLaborMap = new Dictionary<int, decimal>();
            _currentBtMaterialMap = new Dictionary<int, decimal>();
            if (BtSubStagesGrid != null) BtSubStagesGrid.ItemsSource = null;
            UpdateMaterialsPanel(null);

            RefreshBtPresetPicker();
            BuildingTypesList.SelectedItem = null;
            UpdateBuildingDirtyState();
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

        private async Task LoadSubStageLaborsForAssignedPresetsAsync(int buildingTypeId)
        {
            foreach (var list in _btSubStageLabors.Values)
            {
                foreach (var vm in list)
                {
                    vm.PropertyChanged -= SubStageLaborVm_PropertyChanged;
                    vm.Cleanup();
                }
            }

            _btSubStageLabors = new Dictionary<int, ObservableCollection<SubStageLaborVm>>();

            var presetIds = _btAssigned.Select(a => a.StagePresetId).Distinct().ToList();
            if (_db == null || presetIds.Count == 0)
            {
                _currentBtLaborMap = new Dictionary<int, decimal>();
                _currentBtMaterialMap = new Dictionary<int, decimal>();
                return;
            }

            var subPresets = await _db.SubStagePresets
                .Where(s => presetIds.Contains(s.StagePresetId))
                .OrderBy(s => s.OrderIndex)
                .Select(s => new { s.Id, s.StagePresetId, s.Name, s.OrderIndex, s.LaborCost })
                .ToListAsync();

            var subIds = subPresets.Select(s => s.Id).ToList();

            var materialPresets = await _db.MaterialUsagesPreset
                .Where(mu => subIds.Contains(mu.SubStagePresetId))
                .Select(mu => new
                {
                    mu.Id,
                    mu.SubStagePresetId,
                    mu.Qty,
                    MaterialName = mu.Material.Name,
                    mu.Material.Unit
                })
                .ToListAsync();

            var materialsBySubStage = materialPresets
                .GroupBy(m => m.SubStagePresetId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var laborRows = await _db.BuildingTypeSubStageLabors
                .Where(x => x.BuildingTypeId == buildingTypeId)
                .ToListAsync();

            _currentBtLaborMap = laborRows.ToDictionary(x => x.SubStagePresetId, x => x.LaborCost);
            var laborLookup = laborRows.ToDictionary(x => x.SubStagePresetId, x => (decimal?)x.LaborCost);

            var materialRows = await _db.BuildingTypeMaterialUsages
                .Where(x => x.BuildingTypeId == buildingTypeId)
                .ToListAsync();

            _currentBtMaterialMap = materialRows.ToDictionary(x => x.MaterialUsagePresetId, x => x.Qty);
            var materialLookup = materialRows.ToDictionary(x => x.MaterialUsagePresetId, x => (decimal?)x.Qty);

            foreach (var presetId in presetIds)
            {
                var list = new ObservableCollection<SubStageLaborVm>();
                foreach (var sub in subPresets.Where(s => s.StagePresetId == presetId))
                {
                    var vm = new SubStageLaborVm
                    {
                        StagePresetId = presetId,
                        SubStagePresetId = sub.Id,
                        OrderIndex = sub.OrderIndex,
                        Name = sub.Name,
                        LaborCost = laborLookup.TryGetValue(sub.Id, out var labor) ? labor : sub.LaborCost
                    };

                    if (materialsBySubStage.TryGetValue(sub.Id, out var mats))
                    {
                        vm.Materials = new ObservableCollection<MaterialQuantityVm>(
                            mats.Select(m =>
                            {
                                var hasOverride = materialLookup.TryGetValue(m.Id, out var qty);
                                return new MaterialQuantityVm
                                {
                                    MaterialUsagePresetId = m.Id,
                                    Name = m.MaterialName,
                                    Unit = m.Unit,
                                    BaseQty = m.Qty,
                                    Qty = hasOverride ? qty : m.Qty,
                                    IsOverridePersisted = hasOverride,
                                    InitialQty = hasOverride ? qty : m.Qty
                                };
                            }));
                    }
                    else
                    {
                        vm.Materials = new ObservableCollection<MaterialQuantityVm>();
                    }

                    vm.PropertyChanged += SubStageLaborVm_PropertyChanged;
                    list.Add(vm);
                }
                _btSubStageLabors[presetId] = list;
            }
        }

        private async Task<ObservableCollection<SubStageLaborVm>> EnsureSubStageLaborsForPresetAsync(int stagePresetId)
        {
            if (_btSubStageLabors.TryGetValue(stagePresetId, out var existing))
            {
                return existing;
            }

            if (_db == null)
            {
                var empty = new ObservableCollection<SubStageLaborVm>();
                _btSubStageLabors[stagePresetId] = empty;
                return empty;
            }

            var subs = await _db.SubStagePresets
                .Where(s => s.StagePresetId == stagePresetId)
                .OrderBy(s => s.OrderIndex)
                .Select(s => new { s.Id, s.Name, s.OrderIndex, s.LaborCost })
                .ToListAsync();

            var subIds = subs.Select(s => s.Id).ToList();

            var materialPresets = await _db.MaterialUsagesPreset
                .Where(mu => subIds.Contains(mu.SubStagePresetId))
                .Select(mu => new
                {
                    mu.Id,
                    mu.SubStagePresetId,
                    mu.Qty,
                    MaterialName = mu.Material.Name,
                    mu.Material.Unit
                })
                .ToListAsync();

            var materialsBySubStage = materialPresets
                .GroupBy(m => m.SubStagePresetId)
                .ToDictionary(g => g.Key, g => g.ToList());

            Dictionary<int, decimal>? existingLabors = null;
            Dictionary<int, decimal>? existingMaterials = null;

            if (_editingBtId.HasValue)
            {
                if (subIds.Count > 0)
                {
                    existingLabors = await _db.BuildingTypeSubStageLabors
                        .Where(x => x.BuildingTypeId == _editingBtId.Value && subIds.Contains(x.SubStagePresetId))
                        .ToDictionaryAsync(x => x.SubStagePresetId, x => x.LaborCost);
                }

                var materialPresetIds = materialPresets.Select(m => m.Id).ToList();
                if (materialPresetIds.Count > 0)
                {
                    existingMaterials = await _db.BuildingTypeMaterialUsages
                        .Where(x => x.BuildingTypeId == _editingBtId.Value && materialPresetIds.Contains(x.MaterialUsagePresetId))
                        .ToDictionaryAsync(x => x.MaterialUsagePresetId, x => x.Qty);
                }
            }

            var list = new ObservableCollection<SubStageLaborVm>();
            foreach (var sub in subs)
            {
                decimal? labor = null;
                if (existingLabors != null && existingLabors.TryGetValue(sub.Id, out var stored))
                {
                    labor = stored;
                }
                else
                {
                    labor = sub.LaborCost;
                }

                var vm = new SubStageLaborVm
                {
                    StagePresetId = stagePresetId,
                    SubStagePresetId = sub.Id,
                    OrderIndex = sub.OrderIndex,
                    Name = sub.Name,
                    LaborCost = labor
                };

                if (materialsBySubStage.TryGetValue(sub.Id, out var mats))
                {
                    vm.Materials = new ObservableCollection<MaterialQuantityVm>(
                        mats.Select(m =>
                        {
                            var hasOverride = existingMaterials != null && existingMaterials.TryGetValue(m.Id, out var qty);
                            return new MaterialQuantityVm
                            {
                                MaterialUsagePresetId = m.Id,
                                Name = m.MaterialName,
                                Unit = m.Unit,
                                BaseQty = m.Qty,
                                Qty = hasOverride ? qty : m.Qty,
                                IsOverridePersisted = hasOverride,
                                InitialQty = hasOverride ? qty : m.Qty
                            };
                        }));
                }
                else
                {
                    vm.Materials = new ObservableCollection<MaterialQuantityVm>();
                }

                vm.PropertyChanged += SubStageLaborVm_PropertyChanged;
                list.Add(vm);
            }

            _btSubStageLabors[stagePresetId] = list;
            return list;
        }

        private void UpdateBuildingDirtyState()
        {
            if (BtName == null || BtActive == null)
            {
                IsBuildingDirty = false;
                return;
            }

            var name = BtName.Text?.Trim() ?? string.Empty;
            var isActive = BtActive.IsChecked == true;
            var assignedIds = _btAssigned?.Select(a => a.StagePresetId).ToList() ?? new List<int>();

            bool dirty;

            if (_currentBt == null)
            {
                dirty = !string.IsNullOrEmpty(name) || !isActive || assignedIds.Count > 0;
            }
            else
            {
                dirty = !string.Equals(name, _currentBt.Name ?? string.Empty, StringComparison.Ordinal) ||
                        isActive != _currentBt.IsActive;

                if (!dirty)
                {
                    dirty = !_currentBtAssignedIds.SequenceEqual(assignedIds);
                }
            }

            if (!dirty)
            {
                dirty = HaveLaborAssignmentsChanged();
            }

            if (!dirty)
            {
                dirty = HaveMaterialAssignmentsChanged();
            }

            IsBuildingDirty = dirty;
        }

        private Dictionary<int, decimal?> GetCurrentLaborMap()
        {
            var map = new Dictionary<int, decimal?>();
            var assignedIds = _btAssigned.Select(a => a.StagePresetId).ToHashSet();

            foreach (var kvp in _btSubStageLabors)
            {
                if (!assignedIds.Contains(kvp.Key)) continue;
                foreach (var vm in kvp.Value)
                {
                    map[vm.SubStagePresetId] = vm.LaborCost;
                }
            }

            return map;
        }

        private bool HaveLaborAssignmentsChanged()
        {
            var currentMap = GetCurrentLaborMap();

            if (_currentBt == null)
            {
                return currentMap.Count > 0;
            }

            if (currentMap.Count != _currentBtLaborMap.Count)
            {
                return true;
            }

            foreach (var kvp in _currentBtLaborMap)
            {
                if (!currentMap.TryGetValue(kvp.Key, out var value) || !value.HasValue || value.Value != kvp.Value)
                {
                    return true;
                }
            }

            return currentMap.Values.Any(v => !v.HasValue);
        }

        private bool HaveMaterialAssignmentsChanged()
        {
            var assignedIds = _btAssigned.Select(a => a.StagePresetId).ToHashSet();

            foreach (var kvp in _btSubStageLabors)
            {
                if (!assignedIds.Contains(kvp.Key)) continue;
                foreach (var vm in kvp.Value)
                {
                    foreach (var material in vm.Materials)
                    {
                        if (!material.Qty.HasValue)
                        {
                            return true;
                        }
                    }
                }
            }

            var currentMap = GetCurrentMaterialOverrideMap();

            if (_currentBt == null)
            {
                return currentMap.Count > 0;
            }

            if (currentMap.Count != _currentBtMaterialMap.Count)
            {
                return true;
            }

            foreach (var kvp in _currentBtMaterialMap)
            {
                if (!currentMap.TryGetValue(kvp.Key, out var value) || value != kvp.Value)
                {
                    return true;
                }
            }

            return false;
        }

        private Dictionary<int, decimal> GetCurrentMaterialOverrideMap()
        {
            var map = new Dictionary<int, decimal>();
            var assignedIds = _btAssigned.Select(a => a.StagePresetId).ToHashSet();

            foreach (var kvp in _btSubStageLabors)
            {
                if (!assignedIds.Contains(kvp.Key)) continue;
                foreach (var vm in kvp.Value)
                {
                    foreach (var material in vm.Materials)
                    {
                        if (!material.Qty.HasValue)
                        {
                            continue;
                        }

                        var differsFromBase = !material.BaseQty.HasValue || material.Qty.Value != material.BaseQty.Value;
                        var matchesInitial = material.InitialQty.HasValue && material.Qty.Value == material.InitialQty.Value;

                        if (differsFromBase || (material.IsOverridePersisted && matchesInitial))
                        {
                            map[material.MaterialUsagePresetId] = material.Qty.Value;
                        }
                    }
                }
            }

            return map;
        }

        private void SubStageLaborVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SubStageLaborVm.LaborCost) ||
                e.PropertyName == nameof(SubStageLaborVm.Materials))
            {
                UpdateBuildingDirtyState();
            }

            if (sender is SubStageLaborVm vm && ReferenceEquals(vm, _selectedSubStageVm) &&
                e.PropertyName == nameof(SubStageLaborVm.Materials))
            {
                UpdateMaterialsPanel(vm);
            }
        }

        private async void AddPresetToBuildingType_Click(object sender, RoutedEventArgs e)
        {
            if (BtPresetPicker?.SelectedValue is not int presetId)
            {
                MessageBox.Show(ResourceHelper.GetString("AdminHubView_SelectPresetToAddMessage", "Select a stage preset to add."));
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

            await EnsureSubStageLaborsForPresetAsync(preset.Id);

            // ✅ select the newly added preset and show its preview
            BtAssignedGrid.SelectedItem = vm;
            BtAssignedGrid.ScrollIntoView(vm);
        }

        private async void BtAssignedGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var vm = BtAssignedGrid?.SelectedItem as AssignedPresetVm;

            if (vm == null)
            {
                if (BtSelectedPresetTitle != null)
                    BtSelectedPresetTitle.Text = ResourceHelper.GetString(
                        "AdminHubView_SelectedPresetTitle",
                        "Select a preset to preview its sub-stages");
                if (BtSubStagesGrid != null) BtSubStagesGrid.ItemsSource = null;
                UpdateMaterialsPanel(null);
                return;
            }

            if (BtSelectedPresetTitle != null)
                BtSelectedPresetTitle.Text = vm.Name; // show preset name as the header

            if (!_btSubStageLabors.TryGetValue(vm.StagePresetId, out var subs))
            {
                subs = await EnsureSubStageLaborsForPresetAsync(vm.StagePresetId);
            }

            if (BtSubStagesGrid != null)
            {
                BtSubStagesGrid.ItemsSource = subs;
                if (subs.Count > 0)
                {
                    BtSubStagesGrid.SelectedIndex = 0;
                }
                else
                {
                    UpdateMaterialsPanel(null);
                }
            }
        }

        private void BtSubStagesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var vm = BtSubStagesGrid?.SelectedItem as SubStageLaborVm;
            UpdateMaterialsPanel(vm);
        }

        private void UpdateMaterialsPanel(SubStageLaborVm? vm)
        {
            _selectedSubStageVm = vm;

            if (BtSelectedSubStageTitle == null || BtMaterialsGrid == null || BtMaterialsPlaceholder == null)
            {
                if (BtMaterialsGrid != null)
                    BtMaterialsGrid.ItemsSource = vm?.Materials;
                return;
            }

            if (vm == null)
            {
                var prompt = ResourceHelper.GetString("AdminHubView_SelectSubStagePrompt", "Select a sub-stage to edit materials.");
                BtSelectedSubStageTitle.Text = prompt;
                BtMaterialsGrid.ItemsSource = null;
                BtMaterialsPlaceholder.Text = prompt;
                BtMaterialsPlaceholder.Visibility = Visibility.Visible;
                return;
            }

            var titleFormat = ResourceHelper.GetString("AdminHubView_MaterialsForSubStageFormat", "Materials for {0}");
            BtSelectedSubStageTitle.Text = string.Format(titleFormat, vm.Name);
            BtMaterialsGrid.ItemsSource = vm.Materials;

            if (vm.Materials.Count == 0)
            {
                BtMaterialsPlaceholder.Text = ResourceHelper.GetString("AdminHubView_NoMaterialsForSubStage", "This sub-stage has no materials.");
                BtMaterialsPlaceholder.Visibility = Visibility.Visible;
            }
            else
            {
                BtMaterialsPlaceholder.Visibility = Visibility.Collapsed;
            }
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
            if (_btSubStageLabors.TryGetValue(vm.StagePresetId, out var labors))
            {
                foreach (var s in labors)
                {
                    s.PropertyChanged -= SubStageLaborVm_PropertyChanged;
                    s.Cleanup();
                }
                _btSubStageLabors.Remove(vm.StagePresetId);
            }
            _btAssigned.Remove(vm);
            RenumberAssigned();
            RefreshBtPresetPicker();
        }

        private void RenumberAssigned()
        {
            for (int i = 0; i < _btAssigned.Count; i++)
                _btAssigned[i].OrderIndex = i + 1;
            BtAssignedGrid.Items.Refresh();
            UpdateBuildingDirtyState();
        }

        // Save / Cancel
        private async void SaveBuildingType_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null) return;

            var name = BtName?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(ResourceHelper.GetString("AdminHubView_BuildingTypeNameRequired", "Name is required."));
                return;
            }
            var isActive = BtActive?.IsChecked == true;

            using var tx = await _db.Database.BeginTransactionAsync();

            var isCreatingNew = _editingBtId == null;
            BuildingType? createdBuildingType = null;

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
                    createdBuildingType = bt;
                }
                else
                {
                    bt = await _db.BuildingTypes.FirstAsync(x => x.Id == _editingBtId.Value);
                    bt.Name = name;
                    bt.IsActive = isActive;
                    await _db.SaveChangesAsync();
                }

                // Sync join table
                DetachTrackedBuildingTypePresetLinks();

                var existingLinks = await _db.BuildingTypeStagePresets
                    .Where(x => x.BuildingTypeId == _editingBtId!.Value)
                    .AsNoTracking()
                    .ToListAsync();

                var orderedAssignments = _btAssigned
                    .Select((a, index) => new { a.StagePresetId, OrderIndex = index + 1 })
                    .ToList();

                var keepIds = orderedAssignments.Select(x => x.StagePresetId).ToHashSet();
                var toDelete = existingLinks.Where(x => !keepIds.Contains(x.StagePresetId)).ToList();
                if (toDelete.Count > 0)
                {
                    _db.BuildingTypeStagePresets.RemoveRange(toDelete);
                }

                foreach (var assignment in orderedAssignments)
                {
                    var existing = existingLinks.FirstOrDefault(x => x.StagePresetId == assignment.StagePresetId);
                    if (existing == null)
                    {
                        var entity = new BuildingTypeStagePreset
                        {
                            BuildingTypeId = _editingBtId.Value,
                            StagePresetId = assignment.StagePresetId,
                            OrderIndex = assignment.OrderIndex
                        };
                        _db.BuildingTypeStagePresets.Add(entity);
                    }
                    else if (existing.OrderIndex != assignment.OrderIndex)
                    {
                        var stub = new BuildingTypeStagePreset
                        {
                            BuildingTypeId = _editingBtId.Value,
                            StagePresetId = assignment.StagePresetId,
                            OrderIndex = assignment.OrderIndex
                        };
                        _db.BuildingTypeStagePresets.Attach(stub);
                        _db.Entry(stub).Property(x => x.OrderIndex).IsModified = true;
                    }
                }

                await _db.SaveChangesAsync();
                DetachTrackedBuildingTypePresetLinks();

                var assignedPresetIds = _btAssigned.Select(a => a.StagePresetId).ToList();
                foreach (var presetId in assignedPresetIds)
                {
                    var labors = await EnsureSubStageLaborsForPresetAsync(presetId);
                    foreach (var laborVm in labors)
                    {
                        if (!laborVm.LaborCost.HasValue)
                        {
                            MessageBox.Show(
                                ResourceHelper.GetString("AdminHubView_LaborRequired", "Enter a labor cost for every sub-stage before saving."),
                                ResourceHelper.GetString("Common_ValidationTitle", "Validation"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            await tx.RollbackAsync();
                            if (isCreatingNew)
                            {
                                ResetNewBuildingTypeDraft(createdBuildingType);
                            }
                            return;
                        }

                        foreach (var materialVm in laborVm.Materials)
                        {
                            if (!materialVm.Qty.HasValue)
                            {
                                MessageBox.Show(
                                    ResourceHelper.GetString("AdminHubView_MaterialQuantityRequired", "Enter a quantity for every material before saving."),
                                    ResourceHelper.GetString("Common_ValidationTitle", "Validation"),
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                                await tx.RollbackAsync();
                                if (isCreatingNew)
                                {
                                    ResetNewBuildingTypeDraft(createdBuildingType);
                                }
                                return;
                            }
                        }
                    }
                }

                var existingLabors = await _db.BuildingTypeSubStageLabors
                    .Where(x => x.BuildingTypeId == _editingBtId!.Value)
                    .ToListAsync();

                var existingMaterials = await _db.BuildingTypeMaterialUsages
                    .Where(x => x.BuildingTypeId == _editingBtId!.Value)
                    .ToListAsync();

                var keepSubStageIds = assignedPresetIds
                    .SelectMany(id => _btSubStageLabors.TryGetValue(id, out var list)
                        ? list.Select(l => l.SubStagePresetId)
                        : Enumerable.Empty<int>())
                    .ToHashSet();

                var keepMaterialPresetIds = assignedPresetIds
                    .SelectMany(id => _btSubStageLabors.TryGetValue(id, out var list)
                        ? list.SelectMany(l => l.Materials).Select(m => m.MaterialUsagePresetId)
                        : Enumerable.Empty<int>())
                    .ToHashSet();

                var toRemoveLabors = existingLabors
                    .Where(x => !keepSubStageIds.Contains(x.SubStagePresetId))
                    .ToList();

                if (toRemoveLabors.Count > 0)
                {
                    _db.BuildingTypeSubStageLabors.RemoveRange(toRemoveLabors);
                }

                var toRemoveMaterials = existingMaterials
                    .Where(x => !keepMaterialPresetIds.Contains(x.MaterialUsagePresetId))
                    .ToList();

                if (toRemoveMaterials.Count > 0)
                {
                    _db.BuildingTypeMaterialUsages.RemoveRange(toRemoveMaterials);
                    foreach (var removed in toRemoveMaterials)
                        existingMaterials.Remove(removed);
                }

                foreach (var presetId in assignedPresetIds)
                {
                    if (!_btSubStageLabors.TryGetValue(presetId, out var list)) continue;
                    foreach (var laborVm in list)
                    {
                        var cost = laborVm.LaborCost!.Value;
                        var existing = existingLabors.FirstOrDefault(x => x.SubStagePresetId == laborVm.SubStagePresetId);
                        if (existing == null)
                        {
                            var entity = new BuildingTypeSubStageLabor
                            {
                                BuildingTypeId = _editingBtId.Value,
                                SubStagePresetId = laborVm.SubStagePresetId,
                                LaborCost = cost
                            };
                            _db.BuildingTypeSubStageLabors.Add(entity);
                            existingLabors.Add(entity);
                        }
                        else
                        {
                            existing.LaborCost = cost;
                        }

                        foreach (var materialVm in laborVm.Materials)
                        {
                            var qty = materialVm.Qty!.Value;
                            var existingMaterial = existingMaterials.FirstOrDefault(x => x.MaterialUsagePresetId == materialVm.MaterialUsagePresetId);
                            var shouldPersist = !materialVm.BaseQty.HasValue || qty != materialVm.BaseQty.Value;

                            if (!shouldPersist)
                            {
                                if (existingMaterial != null)
                                {
                                    _db.BuildingTypeMaterialUsages.Remove(existingMaterial);
                                    existingMaterials.Remove(existingMaterial);
                                }

                                materialVm.IsOverridePersisted = false;
                                continue;
                            }

                            if (existingMaterial == null)
                            {
                                var entity = new BuildingTypeMaterialUsage
                                {
                                    BuildingTypeId = _editingBtId.Value,
                                    MaterialUsagePresetId = materialVm.MaterialUsagePresetId,
                                    Qty = qty
                                };
                                _db.BuildingTypeMaterialUsages.Add(entity);
                                existingMaterials.Add(entity);
                                materialVm.IsOverridePersisted = true;
                            }
                            else
                            {
                                existingMaterial.Qty = qty;
                                materialVm.IsOverridePersisted = true;
                            }
                        }
                    }
                }

                await _db.SaveChangesAsync();

                _currentBtLaborMap = assignedPresetIds
                    .SelectMany(id => _btSubStageLabors.TryGetValue(id, out var list)
                        ? list
                        : Enumerable.Empty<SubStageLaborVm>())
                    .ToDictionary(vm => vm.SubStagePresetId, vm => vm.LaborCost!.Value);

                _currentBtMaterialMap = GetCurrentMaterialOverrideMap();

                foreach (var kvp in _btSubStageLabors)
                {
                    foreach (var laborVm in kvp.Value)
                    {
                        foreach (var materialVm in laborVm.Materials)
                        {
                            materialVm.IsOverridePersisted = _currentBtMaterialMap.ContainsKey(materialVm.MaterialUsagePresetId);
                            materialVm.InitialQty = materialVm.Qty;
                        }
                    }
                }

                await tx.CommitAsync();

                // refresh caches & UI
                _allBuildingTypes = await _db.BuildingTypes.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
                RefreshBuildingTypesList();

                if (_editingBtId != null)
                {
                    var row = _allBuildingTypes.FirstOrDefault(x => x.Id == _editingBtId.Value);
                    if (row != null) BuildingTypesList.SelectedItem = row;
                }

                MessageBox.Show(
                    ResourceHelper.GetString("AdminHubView_BuildingTypeSavedMessage", "Building type saved."),
                    ResourceHelper.GetString("Common_SuccessTitle", "Success"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                if (isCreatingNew)
                {
                    ResetNewBuildingTypeDraft(createdBuildingType);
                }
                MessageBox.Show(
                    string.Format(ResourceHelper.GetString("AdminHubView_SaveBuildingTypeFailedFormat", "Saving failed:\n{0}"), ex.Message),
                    ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
                _currentBtAssignedIds = new List<int>();
                foreach (var collection in _btSubStageLabors.Values)
                {
                    foreach (var vm in collection)
                    {
                        vm.PropertyChanged -= SubStageLaborVm_PropertyChanged;
                        vm.Cleanup();
                    }
                }
                _btSubStageLabors = new Dictionary<int, ObservableCollection<SubStageLaborVm>>();
                _currentBtLaborMap = new Dictionary<int, decimal>();
                _currentBtMaterialMap = new Dictionary<int, decimal>();
                if (BtSubStagesGrid != null) BtSubStagesGrid.ItemsSource = null;
                UpdateMaterialsPanel(null);
                UpdateBuildingDirtyState();
            }
        }

        private void DetachTrackedBuildingTypePresetLinks()
        {
            if (_db == null) return;

            var tracked = _db.ChangeTracker
                .Entries<BuildingTypeStagePreset>()
                .ToList();

            foreach (var entry in tracked)
            {
                entry.State = EntityState.Detached;
            }
        }

        private void ResetNewBuildingTypeDraft(BuildingType? createdBuildingType)
        {
            if (_db == null) return;

            if (createdBuildingType != null)
            {
                _db.Entry(createdBuildingType).State = EntityState.Detached;
            }

            _editingBtId = null;
            _currentBt = null;
            _currentBtLaborMap = new Dictionary<int, decimal>();
            _currentBtMaterialMap = new Dictionary<int, decimal>();
        }

        private class SubStageLaborVm : INotifyPropertyChanged
        {
            public int StagePresetId { get; set; }
            public int SubStagePresetId { get; set; }
            public int OrderIndex { get; set; }
            public string Name { get; set; } = string.Empty;

            private decimal? _laborCost;
            public decimal? LaborCost
            {
                get => _laborCost;
                set
                {
                    if (_laborCost != value)
                    {
                        _laborCost = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LaborCost)));
                    }
                }
            }

            private ObservableCollection<MaterialQuantityVm> _materials = new();
            public ObservableCollection<MaterialQuantityVm> Materials
            {
                get => _materials;
                set
                {
                    if (!ReferenceEquals(_materials, value))
                    {
                        if (_materials != null)
                        {
                            _materials.CollectionChanged -= Materials_CollectionChanged;
                            foreach (var material in _materials)
                                material.PropertyChanged -= MaterialVm_PropertyChanged;
                        }

                        _materials = value ?? new ObservableCollection<MaterialQuantityVm>();
                        _materials.CollectionChanged += Materials_CollectionChanged;
                        foreach (var material in _materials)
                            material.PropertyChanged += MaterialVm_PropertyChanged;

                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Materials)));
                    }
                }
            }

            public void Cleanup()
            {
                if (_materials != null)
                {
                    _materials.CollectionChanged -= Materials_CollectionChanged;
                    foreach (var material in _materials)
                        material.PropertyChanged -= MaterialVm_PropertyChanged;
                }
            }

            private void Materials_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            {
                if (e.OldItems != null)
                {
                    foreach (MaterialQuantityVm material in e.OldItems)
                        material.PropertyChanged -= MaterialVm_PropertyChanged;
                }

                if (e.NewItems != null)
                {
                    foreach (MaterialQuantityVm material in e.NewItems)
                        material.PropertyChanged += MaterialVm_PropertyChanged;
                }

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Materials)));
            }

            private void MaterialVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(MaterialQuantityVm.Qty))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Materials)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private class MaterialQuantityVm : INotifyPropertyChanged
        {
            public int MaterialUsagePresetId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Unit { get; set; } = string.Empty;
            public decimal? BaseQty { get; set; }
            public bool IsOverridePersisted { get; set; }
            public decimal? InitialQty { get; set; }

            private decimal? _qty;
            public decimal? Qty
            {
                get => _qty;
                set
                {
                    if (_qty != value)
                    {
                        _qty = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Qty)));
                    }
                }
            }

            public string DisplayLabel
            {
                get
                {
                    var baseLabel = string.IsNullOrWhiteSpace(Unit) ? Name : $"{Name} ({Unit})";
                    if (BaseQty.HasValue)
                    {
                        return $"{baseLabel} – Default: {BaseQty.Value:0.##}";
                    }

                    return baseLabel;
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
