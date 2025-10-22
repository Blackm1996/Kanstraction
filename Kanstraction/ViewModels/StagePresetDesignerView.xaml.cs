using Kanstraction;
using Kanstraction.Data;
using Kanstraction.Entities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Kanstraction.ViewModels;

public partial class StagePresetDesignerView : UserControl
{
    private AppDbContext? _db;

    // State
    private int? _currentPresetId = null;
    private StagePreset? _loadedPreset = null;
    private List<Material> _activeMaterials = new(); // for combo
    private HashSet<int> _loadedSubStageIds = new(); // to calculate deletes on save
    private bool _hasPendingLoad = false;
    private int? _pendingPresetId = null;
    // VM collections
    private ObservableCollection<SubStageVm> _subStages = new();
    private SubStageVm? _selectedSubStage = null;
    private object? _subStageOriginalValue = null;
    private string? _subStageOriginalProperty = null;

    public event EventHandler<int>? Saved;

    public StagePresetDesignerView()
    {
        InitializeComponent();
        SubStagesGrid.ItemsSource = _subStages;
        SubStagesGrid.SelectionChanged += (_, __) => UpdateSelectedSubStageFromGrid();
        MaterialsGrid.ItemsSource = new ObservableCollection<MaterialUsageVm>();
        Loaded += StagePresetDesignerView_Loaded;
    }

    private void StagePresetDesignerView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= StagePresetDesignerView_Loaded;

        if (DesignerProperties.GetIsInDesignMode(this))
        {
            return;
        }

        UpdateSummary();
    }
    private void ShowEmptyState()
    {
        EmptyStatePanel.Visibility = Visibility.Visible;
        EditorPanel.Visibility = Visibility.Collapsed;
        ActionsPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowEditor()
    {
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        ActionsPanel.Visibility = Visibility.Visible;
    }

    private void UpdateMaterialsPanelVisibility()
    {
        MaterialsPanel.Visibility = (_selectedSubStage != null) ? Visibility.Visible : Visibility.Collapsed;
    }
    public void EnterEmptyState()
    {
        _currentPresetId = null;
        _loadedPreset = null;
        _subStages.Clear();
        _selectedSubStage = null;
        MaterialsGrid.ItemsSource = new ObservableCollection<MaterialUsageVm>();
        CboMaterial.ItemsSource = null;
        TxtPresetName.Text = "";
        ChkActive.IsChecked = true; // default for when user later clicks "Create New"
        UpdateSummary();
        SetDirty(false);
        ShowEmptyState();
    }

    public void SetDb(AppDbContext db)
    {
        _db = db;
        if (_hasPendingLoad)
        {
            _hasPendingLoad = false;
            var id = _pendingPresetId;   // may be null (new preset)
            _pendingPresetId = null;
            _ = LoadPresetAsync(id);     // fire and forget on UI thread
        }
    }

    // Exposed for AdminHub: load selected preset or new
    public async Task LoadPresetAsync(int? stagePresetId)
    {
        if (_db == null)
        {
            // DB not ready yet: remember the request and return
            _hasPendingLoad = true;
            _pendingPresetId = stagePresetId; // may be null (new preset)
            return;
        }
        ShowEditor();
        _currentPresetId = stagePresetId;
        _loadedPreset = null;
        _loadedSubStageIds.Clear();
        _subStages.Clear();
        _selectedSubStage = null;
        MaterialsGrid.ItemsSource = new ObservableCollection<MaterialUsageVm>();
        CboMaterial.ItemsSource = null;

        // Load active materials (for picker)
        _activeMaterials = await _db.Materials.AsNoTracking()
                               .Where(m => m.IsActive)
                               .Include(m => m.MaterialCategory)
                               .OrderBy(m => m.MaterialCategory != null ? m.MaterialCategory.Name : string.Empty)
                               .ThenBy(m => m.Name)
                               .ToListAsync();

        if (stagePresetId == null)
        {
            // load active materials
            _activeMaterials = await _db.Materials.AsNoTracking()
                .Where(m => m.IsActive)
                .Include(m => m.MaterialCategory)
                .OrderBy(m => m.MaterialCategory != null ? m.MaterialCategory.Name : string.Empty)
                .ThenBy(m => m.Name)
                .ToListAsync();

            TxtPresetName.Text = "";
            ChkActive.IsChecked = true;
            _subStages.Clear();
            _selectedSubStage = null;
            MaterialsGrid.ItemsSource = new ObservableCollection<MaterialUsageVm>();
            CboMaterial.ItemsSource = _activeMaterials;
            CboMaterial.SelectedIndex = -1;

            UpdateSummary();
            UpdateMaterialsPanelVisibility(); // hide materials panel until a sub-stage exists/selected
            SetDirty(false);
            return;
        }

        // Load existing preset with sub-stages and materials
        var preset = await _db.StagePresets
            .AsNoTracking()
            .Include(p => p.SubStages)       
            .Where(p => p.Id == stagePresetId.Value)
            .FirstOrDefaultAsync();

        if (preset == null)
        {
            MessageBox.Show(ResourceHelper.GetString("StagePresetDesignerView_PresetNotFound", "Preset not found."));
            return;
        }

        _loadedPreset = preset;

        TxtPresetName.Text = preset.Name;
        ChkActive.IsChecked = preset.IsActive;

        // Load sub-stages ordered by OrderIndex, and their materials
        var subIds = preset.SubStages.Select(s => s.Id).ToList();

        var usageLookup = await _db.MaterialUsagesPreset
            .Where(mu => subIds.Contains(mu.SubStagePresetId))
            .Include(mu => mu.Material)
                .ThenInclude(m => m.MaterialCategory)
            .AsNoTracking()
            .ToListAsync();

        foreach (var ss in preset.SubStages.OrderBy(s => s.OrderIndex))
        {
            var vm = new SubStageVm
            {
                Id = ss.Id,
                Name = ss.Name,
                OrderIndex = ss.OrderIndex,
                Materials = new ObservableCollection<MaterialUsageVm>()
            };

            foreach (var mu in usageLookup.Where(u => u.SubStagePresetId == ss.Id))
            {
                vm.Materials.Add(new MaterialUsageVm
                {
                    Id = mu.Id,
                    MaterialId = mu.MaterialId,
                    MaterialName = mu.Material?.Name ?? "",
                    CategoryName = mu.Material?.MaterialCategory?.Name ?? "",
                    Unit = mu.Material?.Unit ?? ""
                });
            }

            _subStages.Add(vm);
            _loadedSubStageIds.Add(ss.Id);
        }

        // Select first sub-stage by default
        if (_subStages.Count > 0)
        {
            SubStagesGrid.SelectedIndex = 0;
            _selectedSubStage = _subStages[0];
            MaterialsGrid.ItemsSource = _selectedSubStage.Materials;
        }
        else
        {
            _selectedSubStage = null;
            MaterialsGrid.ItemsSource = new ObservableCollection<MaterialUsageVm>();
        }

        RefreshMaterialPickerItems();
        UpdateMaterialsPanelVisibility(); // <-- here
        UpdateSummary();
        SetDirty(false);
    }

    // -------------------- Dirty tracking --------------------
    public bool IsDirty
    {
        get => (bool)GetValue(IsDirtyProperty);
        set => SetValue(IsDirtyProperty, value);
    }
    public static readonly DependencyProperty IsDirtyProperty =
        DependencyProperty.Register(nameof(IsDirty), typeof(bool), typeof(StagePresetDesignerView), new PropertyMetadata(false));


    private void SetDirty(bool dirty = true) => IsDirty = dirty;

    // -------------------- General field handlers --------------------
    private void TxtPresetName_TextChanged(object sender, TextChangedEventArgs e) => SetDirty();
    private void ChkActive_Changed(object sender, RoutedEventArgs e) => SetDirty();

    // -------------------- Sub-stages actions --------------------
    private void AddSubStage_Click(object sender, RoutedEventArgs e)
    {
        var nextIndex = _subStages.Count + 1;
        var vm = new SubStageVm
        {
            Id = null,
            OrderIndex = nextIndex,
            Materials = new ObservableCollection<MaterialUsageVm>()
        };
        _subStages.Add(vm);
        SubStagesGrid.SelectedItem = vm;
        _selectedSubStage = vm;
        MaterialsGrid.ItemsSource = vm.Materials;
        RefreshMaterialPickerItems();
        UpdateMaterialsPanelVisibility();
        UpdateSummary();
        SetDirty();

        BeginEditingSubStageName(vm);
    }

    private void BeginEditingSubStageName(SubStageVm vm)
    {
        if (vm == null)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var nameColumn = GetSubStageNameColumn();
            if (nameColumn == null)
            {
                return;
            }

            SubStagesGrid.UpdateLayout();
            SubStagesGrid.ScrollIntoView(vm, nameColumn);
            SubStagesGrid.SelectedItem = vm;
            SubStagesGrid.CurrentCell = new DataGridCellInfo(vm, nameColumn);
            SubStagesGrid.Focus();
            SubStagesGrid.BeginEdit();
        }), DispatcherPriority.Background);
    }

    private DataGridColumn? GetSubStageNameColumn()
    {
        foreach (var column in SubStagesGrid.Columns)
        {
            if (column is DataGridBoundColumn boundColumn &&
                boundColumn.Binding is Binding binding &&
                binding.Path?.Path == nameof(SubStageVm.Name))
            {
                return column;
            }
        }

        return SubStagesGrid.Columns.Count > 1 ? SubStagesGrid.Columns[1] : null;
    }

    private void DeleteSubStage_Click(object sender, RoutedEventArgs e)
    {
        var vm = (sender as FrameworkElement)?.Tag as SubStageVm;
        if (vm == null) return;

        if (MessageBox.Show(
                string.Format(ResourceHelper.GetString("StagePresetDesignerView_DeleteSubStageConfirmFormat", "Delete sub-stage '{0}'?"), vm.Name),
                ResourceHelper.GetString("Common_ConfirmTitle", "Confirm"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _subStages.Remove(vm);
        // re-index order
        ReindexOrder();
        // adjust selection
        if (_subStages.Count > 0)
        {
            var newIndex = SubStagesGrid.SelectedIndex;
            if (newIndex < 0)
            {
                newIndex = 0;
            }
            if (newIndex >= _subStages.Count)
            {
                newIndex = _subStages.Count - 1;
            }

            SubStagesGrid.SelectedIndex = newIndex;
        }
        else
        {
            SubStagesGrid.SelectedIndex = -1;
        }
        UpdateSelectedSubStageFromGrid();
        UpdateMaterialsPanelVisibility();
        UpdateSummary();
        SetDirty();
    }

    private void MoveSubStageUp_Click(object sender, RoutedEventArgs e)
    {
        var vm = (sender as FrameworkElement)?.Tag as SubStageVm;
        if (vm == null) return;
        var idx = _subStages.IndexOf(vm);
        if (idx <= 0) return;
        _subStages.Move(idx, idx - 1);
        ReindexOrder();
        SetDirty();
    }

    private void MoveSubStageDown_Click(object sender, RoutedEventArgs e)
    {
        var vm = (sender as FrameworkElement)?.Tag as SubStageVm;
        if (vm == null) return;
        var idx = _subStages.IndexOf(vm);
        if (idx < 0 || idx >= _subStages.Count - 1) return;
        _subStages.Move(idx, idx + 1);
        ReindexOrder();
        SetDirty();
    }

    private void SubStagesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedSubStageFromGrid();
        RefreshMaterialPickerItems();
        UpdateMaterialsPanelVisibility();
    }

    private void UpdateSelectedSubStageFromGrid()
    {
        _selectedSubStage = SubStagesGrid.SelectedItem as SubStageVm;
        if (_selectedSubStage != null)
        {
            MaterialsGrid.ItemsSource = _selectedSubStage.Materials;
        }
        else
        {
            MaterialsGrid.ItemsSource = new ObservableCollection<MaterialUsageVm>();
        }
        RefreshMaterialPickerItems();
    }

    private void ReindexOrder()
    {
        int i = 1;
        foreach (var s in _subStages)
        {
            s.OrderIndex = i++;
        }
    }

    private void EmptyStateCreateNew_Click(object sender, RoutedEventArgs e)
    {
        // Go straight to "new preset" editor
        _ = LoadPresetAsync(null);
    }
    // -------------------- Materials actions (for selected sub-stage) --------------------
    private void AddMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSubStage == null)
        {
            MessageBox.Show(ResourceHelper.GetString("StagePresetDesignerView_SelectSubStageFirst", "Select a sub-stage first."));
            return;
        }

        if (CboMaterial.SelectedValue is not int matId)
        {
            MessageBox.Show(ResourceHelper.GetString("StagePresetDesignerView_SelectMaterial", "Choose a material."));
            return;
        }

        // prevent duplicates
        if (_selectedSubStage.Materials.Any(m => m.MaterialId == matId))
        {
            MessageBox.Show(ResourceHelper.GetString("StagePresetDesignerView_MaterialAlreadyAdded", "This material is already added to the sub-stage."));
            return;
        }

        var mat = _activeMaterials.FirstOrDefault(m => m.Id == matId);
        if (mat == null) return;

        _selectedSubStage.Materials.Add(new MaterialUsageVm
        {
            Id = null,
            MaterialId = mat.Id,
            MaterialName = mat.Name,
            CategoryName = mat.MaterialCategory?.Name ?? "",
            Unit = mat.Unit ?? ""
        });

        // reset add row
        CboMaterial.SelectedIndex = -1;

        RefreshMaterialPickerItems();
        SetDirty();
    }

    private void RemoveMaterial_Click(object sender, RoutedEventArgs e)
    {
        var vm = (sender as FrameworkElement)?.Tag as MaterialUsageVm;
        if (vm == null || _selectedSubStage == null) return;

        _selectedSubStage.Materials.Remove(vm);
        RefreshMaterialPickerItems();
        SetDirty();
    }

    private void RefreshMaterialPickerItems()
    {
        if (_activeMaterials == null) return;

        if (_selectedSubStage == null)
        {
            CboMaterial.ItemsSource = _activeMaterials;  // show all active materials
            return;
        }

        var usedIds = _selectedSubStage.Materials.Select(m => m.MaterialId).ToHashSet();
        var available = _activeMaterials.Where(m => !usedIds.Contains(m.Id)).ToList();
        CboMaterial.ItemsSource = available;
    }

    // -------------------- Summary --------------------
    private void UpdateSummary()
    {
        var totalSubStagesFormat = ResourceHelper.GetString("StagePresetDesignerView_TotalSubStages", "Total sub-stages: {0}");
        TxtTotalSubs.Text = string.Format(totalSubStagesFormat, _subStages.Count);
    }

    // When editing cells, mark dirty and update summary (for labor changes)
    // Hook DataGrid events minimally:
    private void SubStagesGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        _subStageOriginalProperty = null;
        _subStageOriginalValue = null;

        if (e.Row.Item is not SubStageVm vm)
            return;

        if (e.Column is DataGridBoundColumn boundColumn && boundColumn.Binding is Binding binding && binding.Path != null)
        {
            var path = binding.Path.Path;
            if (!string.IsNullOrEmpty(path))
            {
                _subStageOriginalProperty = path;
                _subStageOriginalValue = path switch
                {
                    nameof(SubStageVm.Name) => vm.Name,
                    _ => null
                };

                if (path == nameof(SubStageVm.Name) && e.EditingElement is TextBox textBox)
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }
            }
        }
    }

    private void SubStagesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel)
        {
            _subStageOriginalProperty = null;
            _subStageOriginalValue = null;
            return;
        }

        if (e.Row.Item is not SubStageVm vm)
        {
            _subStageOriginalProperty = null;
            _subStageOriginalValue = null;
            return;
        }

        var property = _subStageOriginalProperty;
        var originalValue = _subStageOriginalValue;

        // After the edit commits, update summary and dirty state
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateSummary();

            bool changed = false;

            if (!string.IsNullOrEmpty(property))
            {
                object? currentValue = property switch
                {
                    nameof(SubStageVm.Name) => vm.Name,
                    _ => null
                };

                changed = !ValuesEqual(originalValue, currentValue);
            }

            if (changed)
                SetDirty();

            _subStageOriginalProperty = null;
            _subStageOriginalValue = null;
        }));
    }

    private static bool ValuesEqual(object? original, object? current)
    {
        if (original == null && current == null) return true;
        if (original == null || current == null) return false;

        if (original is string || current is string)
        {
            var left = (original?.ToString() ?? string.Empty).Trim();
            var right = (current?.ToString() ?? string.Empty).Trim();
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        if (original is decimal od && current is decimal cd)
            return od == cd;

        return Equals(original, current);
    }

    // -------------------- Save / Cancel --------------------
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        var name = TxtPresetName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(ResourceHelper.GetString("StagePresetDesignerView_PresetNameRequired", "Preset name is required."));
            return;
        }

        // validate sub-stages
        foreach (var s in _subStages)
        {
            if (string.IsNullOrWhiteSpace(s.Name))
            {
                MessageBox.Show(ResourceHelper.GetString("StagePresetDesignerView_SubStageNameRequired", "Each sub-stage must have a name."));
                return;
            }

            var duplicateMaterial = s.Materials
                .GroupBy(m => m.MaterialId)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicateMaterial != null)
            {
                var offending = duplicateMaterial.First();
                var message = string.Format(
                    ResourceHelper.GetString(
                        "StagePresetDesignerView_DuplicateMaterialFormat",
                        "\"{0}\" is listed more than once in sub-stage \"{1}\". Remove the duplicate before saving."),
                    offending.MaterialName,
                    s.Name);
                MessageBox.Show(
                    message,
                    ResourceHelper.GetString("Common_ValidationTitle", "Validation"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        using var tx = await _db.Database.BeginTransactionAsync();

        try
        {
            StagePreset preset;
            if (_currentPresetId == null)
            {
                preset = new StagePreset
                {
                    Name = name,
                    IsActive = ChkActive.IsChecked == true
                };
                _db.StagePresets.Add(preset);
                await _db.SaveChangesAsync();
                _currentPresetId = preset.Id;
            }
            else
            {
                preset = await _db.StagePresets.FirstAsync(p => p.Id == _currentPresetId.Value);
                preset.Name = name;
                preset.IsActive = ChkActive.IsChecked == true;
                await _db.SaveChangesAsync();
            }

            // Sync sub-stages:
            // 1) Load existing for this preset
            var existingSubs = await _db.SubStagePresets
                .Where(s => s.StagePresetId == _currentPresetId!.Value)
                .ToListAsync();

            var existingById = existingSubs.ToDictionary(s => s.Id);

            // 2) Delete removed sub-stages (with their materials)
            var currentIds = _subStages.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToHashSet();
            var toDelete = existingSubs.Where(s => !currentIds.Contains(s.Id)).Select(s => s.Id).ToList();
            if (toDelete.Count > 0)
            {
                var usagesToDelete = await _db.MaterialUsagesPreset.Where(mu => toDelete.Contains(mu.SubStagePresetId)).ToListAsync();
                _db.MaterialUsagesPreset.RemoveRange(usagesToDelete);

                var subsToDelete = existingSubs.Where(s => toDelete.Contains(s.Id)).ToList();
                _db.SubStagePresets.RemoveRange(subsToDelete);
                await _db.SaveChangesAsync();
            }

            // 3) Upsert current sub-stages, also build a map VM -> Id
            foreach (var (vm, index) in _subStages.Select((s, i) => (s, i)))
            {
                if (vm.Id == null)
                {
                    var s = new SubStagePreset
                    {
                        StagePresetId = _currentPresetId!.Value,
                        Name = vm.Name,
                        OrderIndex = index + 1
                    };
                    _db.SubStagePresets.Add(s);
                    await _db.SaveChangesAsync();
                    vm.Id = s.Id; // remember for material sync
                }
                else
                {
                    var s = existingById[vm.Id.Value];
                    s.Name = vm.Name;
                    s.OrderIndex = index + 1;
                    await _db.SaveChangesAsync();
                }
            }

            // 4) Sync materials per sub-stage
            var allSubIds = _subStages.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToList();
            var existingUsages = await _db.MaterialUsagesPreset
                .Where(mu => allSubIds.Contains(mu.SubStagePresetId))
                .ToListAsync();

            foreach (var sVm in _subStages)
            {
                if (!sVm.Id.HasValue) continue;
                var sid = sVm.Id.Value;

                var existingForS = existingUsages.Where(mu => mu.SubStagePresetId == sid).ToList();

                // delete removed
                var currentKeys = sVm.Materials.Select(m => (m.Id, m.MaterialId)).ToList();
                var toRemove = existingForS.Where(mu => !currentKeys.Any(k => (k.Id.HasValue && k.Id.Value == mu.Id) || (!k.Id.HasValue && k.MaterialId == mu.MaterialId))).ToList();
                if (toRemove.Count > 0)
                {
                    _db.MaterialUsagesPreset.RemoveRange(toRemove);
                    await _db.SaveChangesAsync();
                }

                // upsert current rows
                foreach (var muVm in sVm.Materials)
                {
                    if (muVm.Id == null)
                    {
                        var row = new MaterialUsagePreset
                        {
                            SubStagePresetId = sid,
                            MaterialId = muVm.MaterialId
                        };
                        _db.MaterialUsagesPreset.Add(row);
                        await _db.SaveChangesAsync();
                        muVm.Id = row.Id;
                    }
                    else
                    {
                        var row = existingForS.First(x => x.Id == muVm.Id.Value);
                        row.MaterialId = muVm.MaterialId; // normally unchanged, but keep consistent
                        await _db.SaveChangesAsync();
                    }
                }
            }

            await tx.CommitAsync();

            SetDirty(false);
            MessageBox.Show(
                ResourceHelper.GetString("StagePresetDesignerView_SavedMessage", "Stage preset saved."),
                ResourceHelper.GetString("StagePresetDesignerView_DialogTitle", "Stage preset"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Saved?.Invoke(this, _currentPresetId!.Value);
        }
        catch (DbUpdateException dbEx) when (IsMaterialPresetUniqueViolation(dbEx))
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                ResourceHelper.GetString(
                    "StagePresetDesignerView_MaterialConflictMessage",
                    "Another user added the same material to this sub-stage. Reload the preset and try again."),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("StagePresetDesignerView_SaveFailedFormat", "Saving failed:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // reload current preset (or clear to new)
        await LoadPresetAsync(_currentPresetId);
        SetDirty(false);
    }

    // -------------------- VMs --------------------
    private class SubStageVm : INotifyPropertyChanged
    {
        private int? _id;
        private string _name = string.Empty;
        private int _orderIndex;
        private ObservableCollection<MaterialUsageVm> _materials = new();

        public int? Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value ?? string.Empty);
        }

        public int OrderIndex
        {
            get => _orderIndex;
            set => SetProperty(ref _orderIndex, value);
        }

        public ObservableCollection<MaterialUsageVm> Materials
        {
            get => _materials;
            set => SetProperty(ref _materials, value ?? new ObservableCollection<MaterialUsageVm>());
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private static bool IsMaterialPresetUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqliteException sqlite && sqlite.SqliteErrorCode == 19)
        {
            return sqlite.Message.IndexOf("MaterialUsagesPreset", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return false;
    }

    private class MaterialUsageVm
    {
        public int? Id { get; set; }
        public int MaterialId { get; set; }
        public string MaterialName { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string Unit { get; set; } = "";
    }
}
