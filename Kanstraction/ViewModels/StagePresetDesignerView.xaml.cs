using Kanstraction.Data;
using Kanstraction.Entities;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

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

    public event EventHandler<int>? Saved;

    public StagePresetDesignerView()
    {
        InitializeComponent();
        SubStagesGrid.ItemsSource = _subStages;
        SubStagesGrid.SelectionChanged += (_, __) => UpdateSelectedSubStageFromGrid();
        MaterialsGrid.ItemsSource = new ObservableCollection<MaterialUsageVm>();
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
                               .OrderBy(m => m.Name)
                               .ToListAsync();

        if (stagePresetId == null)
        {
            // load active materials
            _activeMaterials = await _db.Materials.AsNoTracking()
                .Where(m => m.IsActive)
                .OrderBy(m => m.Name)
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
            MessageBox.Show("Préréglage introuvable.");
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
            .AsNoTracking()
            .ToListAsync();

        foreach (var ss in preset.SubStages.OrderBy(s => s.OrderIndex))
        {
            var vm = new SubStageVm
            {
                Id = ss.Id,
                Name = ss.Name,
                LaborCost = ss.LaborCost,
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
                    Unit = mu.Material?.Unit ?? "",
                    Qty = mu.Qty
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
            Name = "New sub-stage",
            LaborCost = 0,
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
    }

    private void DeleteSubStage_Click(object sender, RoutedEventArgs e)
    {
        var vm = (sender as FrameworkElement)?.Tag as SubStageVm;
        if (vm == null) return;

        if (MessageBox.Show($"Supprimer la sous-étape '{vm.Name}' ?", "Confirmer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _subStages.Remove(vm);
        // re-index order
        ReindexOrder();
        // adjust selection
        if (_subStages.Count > 0)
        {
            SubStagesGrid.SelectedIndex = Math.Min(SubStagesGrid.SelectedIndex, _subStages.Count - 1);
            _selectedSubStage = (SubStageVm)SubStagesGrid.SelectedItem;
            MaterialsGrid.ItemsSource = _selectedSubStage.Materials;
        }
        else
        {
            _selectedSubStage = null;
            MaterialsGrid.ItemsSource = new ObservableCollection<MaterialUsageVm>();
        }
        RefreshMaterialPickerItems();
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
    private void MaterialSelected(object sender, RoutedEventArgs e)
    {
        string unit = "";
        if (CboMaterial.SelectedIndex != -1)
        {
            Material selected = CboMaterial.SelectedItem as Material;
            unit = selected.Unit;
        }
        TxtMTUnit.Text = unit;
    }

    // -------------------- Materials actions (for selected sub-stage) --------------------
    private void AddMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSubStage == null)
        {
            MessageBox.Show("Sélectionnez d'abord une sous-étape.");
            return;
        }

        if (CboMaterial.SelectedValue is not int matId)
        {
            MessageBox.Show("Choisissez un matériau.");
            return;
        }

        if (!decimal.TryParse(TxtQty.Text?.Trim(), out var qty) || qty < 0)
        {
            MessageBox.Show("La quantité doit être un nombre non négatif.");
            return;
        }

        // prevent duplicates
        if (_selectedSubStage.Materials.Any(m => m.MaterialId == matId))
        {
            MessageBox.Show("Ce matériau est déjà ajouté à la sous-étape.");
            return;
        }

        var mat = _activeMaterials.FirstOrDefault(m => m.Id == matId);
        if (mat == null) return;

        _selectedSubStage.Materials.Add(new MaterialUsageVm
        {
            Id = null,
            MaterialId = mat.Id,
            MaterialName = mat.Name,
            Unit = mat.Unit ?? "",
            Qty = qty
        });

        // reset add row
        CboMaterial.SelectedIndex = -1;
        TxtQty.Text = "";

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
        TxtTotalSubs.Text = $"Total sub-stages: {_subStages.Count}";
        var totalLabor = _subStages.Sum(s => s.LaborCost);
        TxtTotalLabor.Text = $"Total default labor: {totalLabor:0.##}";
    }

    // When editing cells, mark dirty and update summary (for labor changes)
    // Hook DataGrid events minimally:
    private void SubStagesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        // After the edit commits, update summary
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateSummary();
            SetDirty();
        }));
    }

    // -------------------- Save / Cancel --------------------
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        var name = TxtPresetName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Le nom du préréglage est requis.");
            return;
        }

        // validate sub-stages
        foreach (var s in _subStages)
        {
            if (string.IsNullOrWhiteSpace(s.Name))
            {
                MessageBox.Show("Chaque sous-étape doit avoir un nom.");
                return;
            }
            if (s.LaborCost < 0)
            {
                MessageBox.Show("Le coût de main-d'œuvre doit être ≥ 0.");
                return;
            }
            foreach (var mu in s.Materials)
            {
                if (mu.Qty < 0)
                {
                    MessageBox.Show("La quantité de matériau doit être ≥ 0.");
                    return;
                }
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
                        LaborCost = vm.LaborCost,
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
                    s.LaborCost = vm.LaborCost;
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
                            MaterialId = muVm.MaterialId,
                            Qty = muVm.Qty
                        };
                        _db.MaterialUsagesPreset.Add(row);
                        await _db.SaveChangesAsync();
                        muVm.Id = row.Id;
                    }
                    else
                    {
                        var row = existingForS.First(x => x.Id == muVm.Id.Value);
                        row.MaterialId = muVm.MaterialId; // normally unchanged, but keep consistent
                        row.Qty = muVm.Qty;
                        await _db.SaveChangesAsync();
                    }
                }
            }

            await tx.CommitAsync();

            SetDirty(false);
            MessageBox.Show("Préréglage enregistré.", "Préréglage d'étape", MessageBoxButton.OK, MessageBoxImage.Information);
            Saved?.Invoke(this, _currentPresetId!.Value);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show("Échec de l'enregistrement :\n" + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // reload current preset (or clear to new)
        await LoadPresetAsync(_currentPresetId);
        SetDirty(false);
    }

    // -------------------- VMs --------------------
    private class SubStageVm
    {
        public int? Id { get; set; }
        public string Name { get; set; } = "";
        public decimal LaborCost { get; set; }
        public int OrderIndex { get; set; }
        public ObservableCollection<MaterialUsageVm> Materials { get; set; } = new();
    }

    private class MaterialUsageVm
    {
        public int? Id { get; set; }
        public int MaterialId { get; set; }
        public string MaterialName { get; set; } = "";
        public string Unit { get; set; } = "";
        public decimal Qty { get; set; }
    }
}
