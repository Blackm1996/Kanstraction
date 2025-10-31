using Kanstraction;
using Kanstraction.Data;
using Kanstraction.Entities;
using Kanstraction.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace Kanstraction.Views;

public partial class OperationsView : UserControl
{
    private AppDbContext? _db;
    private Project? _currentProject;
    public string Breadcrumb
    {
        get => (string)GetValue(BreadcrumbProperty);
        set => SetValue(BreadcrumbProperty, value);
    }
    public static readonly DependencyProperty BreadcrumbProperty =
        DependencyProperty.Register(nameof(Breadcrumb), typeof(string), typeof(OperationsView),
            new PropertyMetadata(ResourceHelper.GetString("OperationsView_SelectProjectPrompt", "Select a project")));

    private int? _currentBuildingId;
    private int? _currentStageId;
    private SubStage? _editingSubStageForLabor;
    private decimal? _originalSubStageLabor;
    private MaterialUsage? _editingMaterialUsage;
    private decimal? _originalMaterialQuantity;
    private int? _pendingStageSelection;
    private List<BuildingRow> _buildingRows = new();
    private ICollectionView? _buildingView;
    private string _buildingSearchText = string.Empty;

    private sealed class BuildingRow
    {
        public int Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public WorkStatus Status { get; init; }
        public int ProgressPercent { get; init; }
        public string CurrentStageName { get; init; } = string.Empty;
        public bool HasPaidItems { get; init; }
    }

    private static string FormatDecimal(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    public OperationsView()
    {
        InitializeComponent();
    }

    public void SetDb(AppDbContext db) => _db = db;

    public async Task ShowProject(Project p)
    {
        if (_db == null) return;

        _currentProject = p;
        Breadcrumb = $"{p.Name}";

        // Clear right-side panels & selection state before loading
        StagesGrid.ItemsSource = null;
        SubStagesGrid.ItemsSource = null;
        MaterialsGrid.ItemsSource = null;
        _currentBuildingId = null;
        _currentStageId = null;
        UpdateSubStageLaborTotal(null);
        UpdateMaterialsTotal(null);

        // One source of truth for loading/selection of buildings
        await ReloadBuildingsAsync();  // (no specific selection; will select first if none)
    }

    private async void BuildingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_db == null) return;
        dynamic? b = BuildingsGrid.SelectedItem;
        if (b == null) return;

        _currentBuildingId = (int)b.Id;

        int? preferredStageId = _pendingStageSelection;
        _pendingStageSelection = null;

        await ReloadStagesAndSubStagesAsync((int)b.Id, preferredStageId);

        Breadcrumb = UpdateBreadcrumbWithBuilding(b.Code);
    }

    private void BuildingSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _buildingSearchText = (sender as TextBox)?.Text ?? string.Empty;
        RefreshBuildingView();
    }

    private async void StagesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_db == null) return;
        dynamic? s = StagesGrid.SelectedItem;
        if (s == null) return;

        _currentStageId = (int)s.Id;

        // Load tracked entities (NO AsNoTracking) so edits persist
        var subStages = await _db.SubStages
            .Where(ss => ss.StageId == _currentStageId)
            .OrderBy(ss => ss.OrderIndex)
            .ToListAsync();

        SubStagesGrid.ItemsSource = subStages;
        UpdateSubStageLaborTotal(subStages);
        var firstSub = subStages.FirstOrDefault();
        if (firstSub != null)
        {
            SubStagesGrid.SelectedItem = firstSub;
            SubStagesGrid.ScrollIntoView(firstSub);

            await LoadMaterialsForSubStageAsync(firstSub);
        }
        else
        {
            MaterialsGrid.ItemsSource = null;
            UpdateMaterialsTotal(null);
        }
    }

    private async void SubStagesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_db == null) return;
        var ss = SubStagesGrid.SelectedItem as SubStage;
        if (ss == null)
        {
            MaterialsGrid.ItemsSource = null;
            UpdateMaterialsTotal(null);
            return;
        }

        await LoadMaterialsForSubStageAsync(ss);
    }

    private async Task LoadMaterialsForSubStageAsync(SubStage subStage)
    {
        if (_db == null)
        {
            MaterialsGrid.ItemsSource = null;
            UpdateMaterialsTotal(null);
            return;
        }

        var usages = await _db.MaterialUsages
            .Include(mu => mu.Material)
                .ThenInclude(m => m.PriceHistory)
            .Include(mu => mu.Material)
                .ThenInclude(m => m.MaterialCategory)
            .Where(mu => mu.SubStageId == subStage.Id)
            .OrderBy(mu => mu.Material.Name)
            .ToListAsync();

        bool freezePrices = subStage.Status == WorkStatus.Finished || subStage.Status == WorkStatus.Paid;

        foreach (var usage in usages)
        {
            usage.DisplayUnitPrice = ComputeUnitPriceForUsage(usage, freezePrices);
        }

        MaterialsGrid.ItemsSource = usages;
        UpdateMaterialsTotal(usages);
    }

    private static decimal ComputeUnitPriceForUsage(MaterialUsage usage, bool freeze)
    {
        if (usage.Material == null)
        {
            return 0m;
        }

        if (!freeze || usage.UsageDate == default)
        {
            return usage.Material.PricePerUnit;
        }

        var usageDate = usage.UsageDate.Date;
        var history = usage.Material.PriceHistory;

        if (history != null && history.Count > 0)
        {
            var applicable = history
                .Where(h => h.StartDate.Date <= usageDate && (!h.EndDate.HasValue || h.EndDate.Value.Date >= usageDate))
                .OrderByDescending(h => h.StartDate)
                .FirstOrDefault();

            if (applicable != null)
            {
                return applicable.PricePerUnit;
            }

            var previous = history
                .Where(h => h.StartDate.Date <= usageDate)
                .OrderByDescending(h => h.StartDate)
                .FirstOrDefault();

            if (previous != null)
            {
                return previous.PricePerUnit;
            }

            var earliest = history
                .OrderBy(h => h.StartDate)
                .FirstOrDefault();

            if (earliest != null)
            {
                return earliest.PricePerUnit;
            }
        }

        return usage.Material.PricePerUnit;
    }

    private void UpdateSubStageLaborTotal(IEnumerable<SubStage>? subStages = null)
    {
        decimal total = 0m;
        var source = subStages ?? SubStagesGrid.ItemsSource as IEnumerable<SubStage>;
        if (source != null)
        {
            foreach (var subStage in source)
            {
                if (subStage != null)
                {
                    total += subStage.LaborCost;
                }
            }
        }

        SubStagesLaborTotalText.Text = FormatDecimal(total);
    }

    private void UpdateMaterialsTotal(IEnumerable<MaterialUsage>? usages = null)
    {
        decimal total = 0m;
        var source = usages ?? MaterialsGrid.ItemsSource as IEnumerable<MaterialUsage>;
        if (source != null)
        {
            foreach (var usage in source)
            {
                if (usage != null)
                {
                    total += usage.Qty * usage.DisplayUnitPrice;
                }
            }
        }

        MaterialsTotalText.Text = FormatDecimal(total);
    }

    private void ChangeStageStatus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.ContextMenu == null) return;

        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.Placement = PlacementMode.Bottom;
        btn.ContextMenu.IsOpen = true;
    }

    private async void StageStatusMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;
        if (sender is not MenuItem menuItem) return;
        if (menuItem.Tag is not WorkStatus newStatus) return;

        int stageId;
        try
        {
            dynamic ctx = menuItem.DataContext;
            stageId = (int)ctx.Id;
        }
        catch
        {
            return;
        }

        await ChangeStageStatusAsync(stageId, newStatus);
    }

    private async Task ChangeStageStatusAsync(int stageId, WorkStatus newStatus)
    {
        if (_db == null) return;

        try
        {
            var stage = await _db.Stages
                .Include(s => s.SubStages)
                    .ThenInclude(ss => ss.MaterialUsages)
                .Include(s => s.Building)
                    .ThenInclude(b => b.Stages)
                .FirstOrDefaultAsync(s => s.Id == stageId);

            if (stage == null)
                return;

            if (stage.SubStages != null)
            {
                foreach (var sub in stage.SubStages)
                {
                    sub.Status = newStatus;

                    switch (newStatus)
                    {
                        case WorkStatus.NotStarted:
                            sub.StartDate = null;
                            sub.EndDate = null;
                            break;
                        case WorkStatus.Ongoing:
                            if (sub.StartDate == null)
                                sub.StartDate = DateTime.Today;
                            sub.EndDate = null;
                            break;
                        case WorkStatus.Finished:
                        case WorkStatus.Paid:
                            if (sub.StartDate == null)
                                sub.StartDate = DateTime.Today;
                            sub.EndDate = DateTime.Today;
                            if (sub.MaterialUsages != null)
                            {
                                var freezeDate = sub.EndDate.Value.Date;
                                foreach (var usage in sub.MaterialUsages)
                                {
                                    usage.UsageDate = freezeDate;
                                }
                            }
                            break;
                        case WorkStatus.Stopped:
                            if (sub.StartDate == null)
                                sub.StartDate = DateTime.Today;
                            sub.EndDate = DateTime.Today;
                            break;
                    }
                }
            }

            stage.Status = newStatus;

            switch (newStatus)
            {
                case WorkStatus.NotStarted:
                    stage.StartDate = null;
                    stage.EndDate = null;
                    break;
                case WorkStatus.Ongoing:
                    if (stage.StartDate == null)
                        stage.StartDate = DateTime.Today;
                    stage.EndDate = null;
                    break;
                case WorkStatus.Finished:
                case WorkStatus.Paid:
                    if (stage.StartDate == null)
                        stage.StartDate = DateTime.Today;
                    stage.EndDate = DateTime.Today;
                    break;
                case WorkStatus.Stopped:
                    if (stage.StartDate == null)
                        stage.StartDate = DateTime.Today;
                    stage.EndDate = DateTime.Today;
                    break;
            }

            UpdateBuildingStatusFromStages(stage.Building);
            await _db.SaveChangesAsync();

            await ReloadBuildingsAsync(stage.BuildingId, stage.Id);
            await ReloadStagesAndSubStagesAsync(stage.BuildingId, stage.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_ChangeStageStatusFailedFormat", "Failed to change stage status:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    private void SubStagesGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.Column is DataGridTextColumn column &&
            column.Binding is Binding binding &&
            binding.Path?.Path == nameof(SubStage.LaborCost) &&
            e.Row.Item is SubStage ss)
        {
            _editingSubStageForLabor = ss;
            _originalSubStageLabor = ss.LaborCost;
        }
        else
        {
            _editingSubStageForLabor = null;
            _originalSubStageLabor = null;
        }
    }

    private async void SubStagesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_db == null) return;
        if (e.EditAction != DataGridEditAction.Commit)
        {
            if (e.Row.Item is SubStage subStage && ReferenceEquals(_editingSubStageForLabor, subStage))
            {
                _editingSubStageForLabor = null;
                _originalSubStageLabor = null;
            }

            return;
        }

        if (e.Column is DataGridTextColumn column &&
            column.Binding is Binding binding &&
            binding.Path?.Path == nameof(SubStage.LaborCost) &&
            e.Row.Item is SubStage ss)
        {
            decimal originalValue;
            if (_editingSubStageForLabor == ss && _originalSubStageLabor.HasValue)
            {
                originalValue = _originalSubStageLabor.Value;
            }
            else
            {
                originalValue = _db.Entry(ss).Property(s => s.LaborCost).OriginalValue;
            }

            if (ss.LaborCost < 0)
            {
                MessageBox.Show(
                    ResourceHelper.GetString("OperationsView_LaborCostNegative", "Labor cost cannot be negative."),
                    ResourceHelper.GetString("Common_ValidationTitle", "Validation"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ss.LaborCost = 0;
                if (e.EditingElement is TextBox negativeTextBox)
                {
                    negativeTextBox.Text = FormatDecimal(ss.LaborCost);
                }
            }

            var newValue = ss.LaborCost;

            if (originalValue == newValue)
            {
                _db.Entry(ss).Property(s => s.LaborCost).IsModified = false;
                _editingSubStageForLabor = null;
                _originalSubStageLabor = null;
                UpdateSubStageLaborTotal();
                return;
            }

            var message = string.Format(
                CultureInfo.CurrentCulture,
                ResourceHelper.GetString("OperationsView_ConfirmLaborChangeMessage", "Are you sure you want to change the labor from {0} to {1}?"),
                FormatDecimal(originalValue),
                FormatDecimal(newValue));

            var dialog = new ConfirmValueChangeDialog(
                ResourceHelper.GetString("OperationsView_ConfirmChangeTitle", "Save"),
                message,
                ResourceHelper.GetString("Common_Save", "Save"),
                ResourceHelper.GetString("OperationsView_ReturnToDefault", "Cancel"))
            {
                Owner = Window.GetWindow(this)
            };

            var result = dialog.ShowDialog();

            bool refreshTotal = false;
            if (result == true)
            {
                await _db.SaveChangesAsync();
                refreshTotal = true;
            }
            else
            {
                ss.LaborCost = originalValue;
                if (e.EditingElement is TextBox textBox)
                {
                    textBox.Text = FormatDecimal(originalValue);
                }

                var entry = _db.Entry(ss);
                entry.Property(s => s.LaborCost).CurrentValue = originalValue;
                entry.Property(s => s.LaborCost).IsModified = false;
                refreshTotal = true;
            }

            _editingSubStageForLabor = null;
            _originalSubStageLabor = null;

            if (refreshTotal)
            {
                UpdateSubStageLaborTotal();
            }
        }
    }

    private void MaterialsGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.Column is DataGridTextColumn column &&
            column.Binding is Binding binding &&
            binding.Path?.Path == nameof(MaterialUsage.Qty) &&
            e.Row.Item is MaterialUsage mu)
        {
            _editingMaterialUsage = mu;
            _originalMaterialQuantity = mu.Qty;
        }
        else
        {
            _editingMaterialUsage = null;
            _originalMaterialQuantity = null;
        }
    }

    private async void MaterialsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_db == null) return;
        if (e.EditAction != DataGridEditAction.Commit)
        {
            if (e.Row.Item is MaterialUsage materialUsage && ReferenceEquals(_editingMaterialUsage, materialUsage))
            {
                _editingMaterialUsage = null;
                _originalMaterialQuantity = null;
            }

            return;
        }

        if (e.Column is DataGridTextColumn column &&
            column.Binding is Binding binding &&
            binding.Path?.Path == nameof(MaterialUsage.Qty) &&
            e.Row.Item is MaterialUsage mu)
        {
            decimal originalValue;
            if (_editingMaterialUsage == mu && _originalMaterialQuantity.HasValue)
            {
                originalValue = _originalMaterialQuantity.Value;
            }
            else
            {
                originalValue = _db.Entry(mu).Property(m => m.Qty).OriginalValue;
            }

            if (mu.Qty < 0)
            {
                MessageBox.Show(
                    ResourceHelper.GetString("OperationsView_QuantityNegative", "Quantity cannot be negative."),
                    ResourceHelper.GetString("Common_ValidationTitle", "Validation"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                mu.Qty = 0;
                if (e.EditingElement is TextBox negativeTextBox)
                {
                    negativeTextBox.Text = FormatDecimal(mu.Qty);
                }
            }

            var newValue = mu.Qty;

            if (originalValue == newValue)
            {
                _db.Entry(mu).Property(m => m.Qty).IsModified = false;
                _editingMaterialUsage = null;
                _originalMaterialQuantity = null;
                UpdateMaterialsTotal();
                return;
            }

            var message = string.Format(
                CultureInfo.CurrentCulture,
                ResourceHelper.GetString("OperationsView_ConfirmMaterialChangeMessage", "Are you sure you want to change the quantity from {0} to {1}?"),
                FormatDecimal(originalValue),
                FormatDecimal(newValue));

            var dialog = new ConfirmValueChangeDialog(
                ResourceHelper.GetString("OperationsView_ConfirmChangeTitle", "Confirm change"),
                message,
                ResourceHelper.GetString("Common_Save", "Save"),
                ResourceHelper.GetString("OperationsView_ReturnToDefault", "Return to default"))
            {
                Owner = Window.GetWindow(this)
            };

            var result = dialog.ShowDialog();

            bool refreshTotal = false;
            if (result == true)
            {
                await _db.SaveChangesAsync();
                refreshTotal = true;
            }
            else
            {
                mu.Qty = originalValue;
                if (e.EditingElement is TextBox textBox)
                {
                    textBox.Text = FormatDecimal(originalValue);
                }

                var entry = _db.Entry(mu);
                entry.Property(m => m.Qty).CurrentValue = originalValue;
                entry.Property(m => m.Qty).IsModified = false;
                refreshTotal = true;
            }

            _editingMaterialUsage = null;
            _originalMaterialQuantity = null;

            if (refreshTotal)
            {
                UpdateMaterialsTotal();
            }
        }
    }
    private string UpdateBreadcrumbWithBuilding(string code)
    {
        var baseText = Breadcrumb;
        var idx = baseText.IndexOf(" > ", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) baseText = baseText.Substring(0, idx);
        return $"{baseText} > {code}";
    }

    private async void AddBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectProjectFirst", "Select a project first."),
                ResourceHelper.GetString("OperationsView_NoProjectTitle", "No project"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Dialog to collect code + building type
        var dlg = new AddBuildingDialog(_db) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        var code = dlg.BuildingCode?.Trim();
        var typeId = dlg.BuildingTypeId;
        if (string.IsNullOrWhiteSpace(code) || typeId == null) return;

        // Enforce unique code per project (nice safeguard)
        var exists = await _db.Buildings.AnyAsync(b => b.ProjectId == _currentProject.Id && b.Code == code);
        if (exists)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_DuplicateCodeMessage", "A building with this code already exists in this project."),
                ResourceHelper.GetString("OperationsView_DuplicateCodeTitle", "Duplicate code"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // 1) Create the building
            var building = new Building
            {
                ProjectId = _currentProject.Id,
                BuildingTypeId = typeId.Value,
                Code = code!,
                Status = WorkStatus.NotStarted
            };
            _db.Buildings.Add(building);

            // 2) Get ordered stage preset ids for the selected building type
            var stagePresetIds = await _db.BuildingTypeStagePresets
                .Where(x => x.BuildingTypeId == typeId.Value)
                .OrderBy(x => x.OrderIndex)
                .Select(x => x.StagePresetId)
                .ToListAsync();

            var laborLookup = await _db.BuildingTypeSubStageLabors
                .Where(x => x.BuildingTypeId == typeId.Value && x.LaborCost.HasValue)
                .ToDictionaryAsync(x => x.SubStagePresetId, x => x.LaborCost!.Value);

            var materialLookup = await _db.BuildingTypeMaterialUsages
                .Where(x => x.BuildingTypeId == typeId.Value && x.Qty.HasValue)
                .ToDictionaryAsync(x => (x.SubStagePresetId, x.MaterialId), x => x.Qty!.Value);

            int stageOrder = 1;

            foreach (var presetId in stagePresetIds)
            {
                // Load the stage preset name
                var stageName = await _db.StagePresets
                    .Where(p => p.Id == presetId)
                    .Select(p => p.Name)
                    .FirstAsync();

                // Create Stage (attach to building via navigation; EF will set FK)
                var stage = new Stage
                {
                    Building = building,
                    Name = stageName,
                    OrderIndex = stageOrder++,
                    Status = WorkStatus.NotStarted
                };
                _db.Stages.Add(stage);

                // Load sub-stage presets for this stage preset (ordered)
                var subPresets = await _db.SubStagePresets
                    .Where(s => s.StagePresetId == presetId)
                    .OrderBy(s => s.OrderIndex)
                    .ToListAsync();

                foreach (var sp in subPresets)
                {
                    // Create SubStage
                    var sub = new SubStage
                    {
                        Stage = stage,
                        Name = sp.Name,
                        OrderIndex = sp.OrderIndex,
                        Status = WorkStatus.NotStarted,
                        LaborCost = laborLookup.TryGetValue(sp.Id, out var labor)
                            ? labor
                            : 0m
                    };
                    _db.SubStages.Add(sub);

                    // Copy MATERIALS from MaterialUsagePreset → MaterialUsage
                    var muPresets = await _db.MaterialUsagesPreset
                        .Where(mu => mu.SubStagePresetId == sp.Id)
                        .ToListAsync();

                    foreach (var mup in muPresets)
                    {
                        var qty = materialLookup.TryGetValue((sp.Id, mup.MaterialId), out var btQty)
                            ? btQty
                            : 0m;

                        // Seed with today's date; you can edit later
                        var mu = new MaterialUsage
                        {
                            SubStage = sub,                  // link via navigation
                            MaterialId = mup.MaterialId,
                            Qty = qty,
                            UsageDate = DateTime.Today,
                            Notes = null
                        };
                        _db.MaterialUsages.Add(mu);
                    }
                }
            }

            // 3) Commit all inserts in one shot
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            // 4) Refresh grid and select new building
            await ReloadBuildingsAsync(building.Id);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_CreateBuildingFailedFormat", "Failed to create building:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ReloadBuildingsAsync(int? selectBuildingId = null, int? preferredStageId = null)
    {
        if (_currentProject == null || _db == null)
        {
            return;
        }

        var buildings = await _db.Buildings
            .Where(b => b.ProjectId == _currentProject.Id)
            .Include(b => b.BuildingType)
            .Include(b => b.Stages)
                .ThenInclude(s => s.SubStages)
            .AsNoTracking()
            .ToListAsync();

        _buildingRows = buildings
            .Select(b => new BuildingRow
            {
                Id = b.Id,
                Code = b.Code,
                TypeName = b.BuildingType?.Name ?? string.Empty,
                Status = b.Status,
                ProgressPercent = ComputeBuildingProgress(b),
                CurrentStageName = ComputeCurrentStageName(b),
                HasPaidItems = BuildingHasPaidItems(b)
            })
            .OrderBy(x => x.Id)
            .ToList();

        _buildingView = CollectionViewSource.GetDefaultView(_buildingRows);
        if (_buildingView != null)
        {
            _buildingView.Filter = BuildingFilter;
            BuildingsGrid.ItemsSource = _buildingView;
        }
        else
        {
            BuildingsGrid.ItemsSource = _buildingRows;
        }

        BuildingRow? desiredSelection = null;
        if (selectBuildingId.HasValue)
        {
            desiredSelection = _buildingRows.FirstOrDefault(x => x.Id == selectBuildingId.Value);
        }

        RefreshBuildingView(desiredSelection, preferredStageId);
    }

    private void RefreshBuildingView(BuildingRow? desiredSelection = null, int? preferredStageId = null)
    {
        if (_buildingView == null)
        {
            BuildingsGrid.ItemsSource = _buildingRows;

            if (desiredSelection != null)
            {
                _pendingStageSelection = preferredStageId;
                BuildingsGrid.SelectedItem = desiredSelection;
                BuildingsGrid.ScrollIntoView(desiredSelection);
                return;
            }

            if (BuildingsGrid.SelectedItem == null && _buildingRows.Count > 0)
            {
                _pendingStageSelection = null;
                BuildingsGrid.SelectedItem = _buildingRows[0];
                BuildingsGrid.ScrollIntoView(_buildingRows[0]);
                return;
            }

            if (BuildingsGrid.SelectedItem == null)
            {
                StagesGrid.ItemsSource = null;
                SubStagesGrid.ItemsSource = null;
                MaterialsGrid.ItemsSource = null;
                _currentBuildingId = null;
                _currentStageId = null;
                UpdateSubStageLaborTotal(null);
                UpdateMaterialsTotal(null);
            }

            return;
        }

        _buildingView.Refresh();
        var filtered = _buildingView.Cast<BuildingRow>().ToList();

        if (desiredSelection != null)
        {
            if (filtered.Contains(desiredSelection))
            {
                _pendingStageSelection = preferredStageId;
                BuildingsGrid.SelectedItem = desiredSelection;
                BuildingsGrid.ScrollIntoView(desiredSelection);
                return;
            }

            _pendingStageSelection = null;
        }

        if (filtered.Count == 0)
        {
            BuildingsGrid.SelectedItem = null;
            StagesGrid.ItemsSource = null;
            SubStagesGrid.ItemsSource = null;
            MaterialsGrid.ItemsSource = null;
            _currentBuildingId = null;
            _currentStageId = null;
            UpdateSubStageLaborTotal(null);
            UpdateMaterialsTotal(null);
            return;
        }

        if (BuildingsGrid.SelectedItem is BuildingRow current && filtered.Contains(current))
        {
            return;
        }

        var first = filtered[0];
        _pendingStageSelection = null;
        BuildingsGrid.SelectedItem = first;
        BuildingsGrid.ScrollIntoView(first);
    }

    private bool BuildingFilter(object? obj)
    {
        if (obj is not BuildingRow row)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_buildingSearchText))
        {
            return true;
        }

        return row.Code?.IndexOf(_buildingSearchText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static decimal CompletionValue(WorkStatus status) =>
        status == WorkStatus.Finished || status == WorkStatus.Paid || status == WorkStatus.Stopped ? 1m : 0m;

    private static int ComputeBuildingProgress(Building b)
    {
        if (b.Stages == null || b.Stages.Count == 0)
            return (int)Math.Round(CompletionValue(b.Status) * 100m, MidpointRounding.AwayFromZero);

        decimal perStageWeight = 1m / b.Stages.Count;
        decimal sum = 0m;

        foreach (var s in b.Stages.OrderBy(s => s.OrderIndex))
        {
            decimal stageFraction;

            if (s.SubStages == null || s.SubStages.Count == 0)
            {
                stageFraction = CompletionValue(s.Status);
            }
            else
            {
                stageFraction = ComputeStageProgress(s) / 100m;
            }

            sum += perStageWeight * stageFraction;
        }

        return (int)Math.Round(sum * 100m, MidpointRounding.AwayFromZero);
    }

    private static int ComputeStageProgress(Stage stage)
    {
        if (stage.SubStages == null || stage.SubStages.Count == 0)
            return (int)Math.Round(CompletionValue(stage.Status) * 100m, MidpointRounding.AwayFromZero);

        var orderedSubs = stage.SubStages
            .OrderBy(ss => ss.OrderIndex)
            .ToList();

        if (orderedSubs.Count == 0)
            return (int)Math.Round(CompletionValue(stage.Status) * 100m, MidpointRounding.AwayFromZero);

        decimal perSub = 1m / orderedSubs.Count;
        decimal sum = 0m;

        foreach (var ss in orderedSubs)
            sum += perSub * CompletionValue(ss.Status);

        return (int)Math.Round(sum * 100m, MidpointRounding.AwayFromZero);
    }

    private static string ComputeCurrentSubStageName(Stage stage)
    {
        if (stage.Status == WorkStatus.Finished || stage.Status == WorkStatus.Paid || stage.Status == WorkStatus.Stopped)
            return string.Empty;

        if (stage.SubStages == null || stage.SubStages.Count == 0)
            return string.Empty;

        var ongoing = stage.SubStages
            .OrderBy(ss => ss.OrderIndex)
            .FirstOrDefault(ss => ss.Status == WorkStatus.Ongoing);

        return ongoing?.Name ?? string.Empty;
    }

    private static bool BuildingHasPaidItems(Building b)
    {
        if (b.Status == WorkStatus.Paid)
            return true;

        if (b.Stages == null || b.Stages.Count == 0)
            return false;

        foreach (var stage in b.Stages)
        {
            if (stage.Status == WorkStatus.Paid)
                return true;

            if (stage.SubStages == null)
                continue;

            if (stage.SubStages.Any(ss => ss.Status == WorkStatus.Paid))
                return true;
        }

        return false;
    }

    private static string ComputeCurrentStageName(Building b)
    {
        if (b.Stages == null || b.Stages.Count == 0) return "";

        // "Current" = first stage that is not fully done (i.e., not Finished/Paid/Stopped);
        // else the last stage name.
        foreach (var s in b.Stages.OrderBy(s => s.OrderIndex))
        {
            bool stageDone = s.Status == WorkStatus.Finished || s.Status == WorkStatus.Paid || s.Status == WorkStatus.Stopped;

            if (s.SubStages != null && s.SubStages.Count > 0)
            {
                // If any sub-stage is not fully done, stage is current
                stageDone = s.SubStages.All(ss => ss.Status == WorkStatus.Finished || ss.Status == WorkStatus.Paid || ss.Status == WorkStatus.Stopped);
            }

            if (!stageDone) return s.Name;
        }
        return b.Stages.OrderBy(s => s.OrderIndex).Last().Name;
    }

    private async Task SeedStagesForBuildingAsync(int buildingId, int buildingTypeId)
    {
        // Get ordered stage presets for the building type
        var links = await _db.BuildingTypeStagePresets
            .Where(x => x.BuildingTypeId == buildingTypeId)
            .OrderBy(x => x.OrderIndex)
            .Include(x => x.StagePreset)
            .ToListAsync();

        // Fetch all sub-stages for the involved presets in one go
        var presetIds = links.Select(l => l.StagePresetId).ToList();
        var subStagesByPreset = await _db.SubStagePresets
            .Where(s => presetIds.Contains(s.StagePresetId))
            .OrderBy(s => s.OrderIndex)
            .GroupBy(s => s.StagePresetId)
            .ToDictionaryAsync(g => g.Key, g => g.ToList());

        var laborLookup = await _db.BuildingTypeSubStageLabors
            .Where(x => x.BuildingTypeId == buildingTypeId)
            .ToDictionaryAsync(x => x.SubStagePresetId, x => x.LaborCost);

        int stageOrder = 1;

        foreach (var link in links)
        {
            var preset = link.StagePreset!;
            var newStage = new Stage
            {
                BuildingId = buildingId,
                Name = preset.Name,
                OrderIndex = stageOrder++,
                Status = WorkStatus.NotStarted,
                StartDate = null,
                EndDate = null,
                Notes = ""
            };
            _db.Stages.Add(newStage);
            await _db.SaveChangesAsync(); // need Id for sub-stages

            // Sub-stages for this preset (if any)
            if (subStagesByPreset.TryGetValue(preset.Id, out var subs))
            {
                int subOrder = 1;
                foreach (var ssp in subs)
                {
                    var newSub = new SubStage
                    {
                        StageId = newStage.Id,
                        Name = ssp.Name,
                        OrderIndex = subOrder++,
                        Status = WorkStatus.NotStarted,
                        StartDate = null,
                        EndDate = null,
                        LaborCost = laborLookup.TryGetValue(ssp.Id, out var labor) && labor.HasValue
                            ? labor.Value
                            : 0m
                    };
                    _db.SubStages.Add(newSub);
                }
                await _db.SaveChangesAsync();
            }
        }
    }

    private async Task ReloadBuildingsForCurrentProjectAsync(int? selectBuildingId = null)
    {
        if (_db == null || _currentProject == null) return;

        // your existing loader (projected columns + current stage name + progress)
        await ShowProject(_currentProject);

        if (selectBuildingId.HasValue)
        {
            var list = BuildingsGrid.ItemsSource as System.Collections.IEnumerable;
            if (list != null)
            {
                foreach (var item in list)
                {
                    var prop = item.GetType().GetProperty("Id");
                    if (prop != null && (int)prop.GetValue(item)! == selectBuildingId.Value)
                    {
                        BuildingsGrid.SelectedItem = item;
                        BuildingsGrid.ScrollIntoView(item);
                        break;
                    }
                }
            }
        }
    }

    private async void StopBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null || _currentProject == null) return;
        // Find selected building (from the anonymous row)
        var row = BuildingsGrid.SelectedItem as dynamic;
        if (row == null) return;
        int buildingId = row.Id;

        var confirm = MessageBox.Show(
            ResourceHelper.GetString("OperationsView_StopBuildingConfirmMessage", "Stop this building? All unfinished/unpaid stages and sub-stages will be marked Stopped."),
            ResourceHelper.GetString("OperationsView_StopBuildingConfirmTitle", "Confirm stop"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var b = await _db.Buildings
                .Include(b => b.Stages)
                    .ThenInclude(s => s.SubStages)
                .FirstAsync(b => b.Id == buildingId);

            b.Status = WorkStatus.Stopped;

            foreach (var s in b.Stages)
            {
                if (s.Status != WorkStatus.Finished && s.Status != WorkStatus.Paid)
                    s.Status = WorkStatus.Stopped;

                foreach (var ss in s.SubStages)
                {
                    if (ss.Status != WorkStatus.Finished && ss.Status != WorkStatus.Paid)
                        ss.Status = WorkStatus.Stopped;
                }
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            // Refresh UI (keep selection)
            await ReloadBuildingsAsync(buildingId);
            await ReloadStagesAndSubStagesAsync(buildingId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_StopBuildingFailedFormat", "Failed to stop building:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void DeleteBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null || _currentProject == null) return;

        if (sender is not Button btn) return;

        if (btn.Tag is not int buildingId) return;

        var building = await _db.Buildings
            .Include(b => b.Stages)
                .ThenInclude(s => s.SubStages)
            .FirstOrDefaultAsync(b => b.Id == buildingId);

        if (building == null) return;

        if (BuildingHasPaidItems(building))
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_DeleteBuildingPaidMessage", "This building has paid items and cannot be deleted."),
                ResourceHelper.GetString("OperationsView_DeleteBuildingTitle", "Delete building"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            string.Format(ResourceHelper.GetString("OperationsView_DeleteBuildingConfirmFormat", "Delete building '{0}'? All stages and sub-stages will be removed."), building.Code),
            ResourceHelper.GetString("OperationsView_DeleteBuildingTitle", "Delete building"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            _db.Buildings.Remove(building);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_DeleteBuildingFailedFormat", "Failed to delete building:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (_currentBuildingId == buildingId)
        {
            _currentBuildingId = null;
            _currentStageId = null;
            _pendingStageSelection = null;
            StagesGrid.ItemsSource = null;
            SubStagesGrid.ItemsSource = null;
            MaterialsGrid.ItemsSource = null;
            UpdateSubStageLaborTotal(null);
            UpdateMaterialsTotal(null);
            Breadcrumb = _currentProject.Name;
        }

        await ReloadBuildingsAsync();
    }

    private async void StartSubStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        // assuming SubStagesGrid binds to SubStage entities (not anonymous)
        var sel = SubStagesGrid.SelectedItem as SubStage;
        if (sel == null) return;

        // Load full entity + its stage and building
        var ss = await _db.SubStages
            .Include(x => x.Stage)
                .ThenInclude(s => s.Building)
            .FirstAsync(x => x.Id == sel.Id);

        // Rule: only one Ongoing sub-stage in the building
        bool hasOngoingElsewhere = await _db.SubStages
            .AnyAsync(x => x.Stage.BuildingId == ss.Stage.BuildingId && x.Status == WorkStatus.Ongoing && x.Id != ss.Id);
        if (hasOngoingElsewhere)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SubStageAlreadyInProgress", "Another sub-stage is already in progress in this building. Finish it first."),
                ResourceHelper.GetString("OperationsView_RuleTitle", "Rule"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // ❗ Order rule #1: previous sub-stage (same stage) must be Finished or Paid
        if (ss.OrderIndex > 1)
        {
            var prevSub = await _db.SubStages
                .Where(x => x.StageId == ss.StageId && x.OrderIndex == ss.OrderIndex - 1)
                .Select(x => new { x.Id, x.Status })
                .FirstOrDefaultAsync();

            if (prevSub == null || (prevSub.Status != WorkStatus.Finished && prevSub.Status != WorkStatus.Paid))
            {
                MessageBox.Show(
                    ResourceHelper.GetString("OperationsView_PreviousSubStageRequired", "You must finish the previous sub-stage first."),
                    ResourceHelper.GetString("OperationsView_RuleOrderTitle", "Order rule"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        // ❗ Order rule #2: all earlier stages must be Finished or Paid
        if (ss.Stage.OrderIndex > 1)
        {
            bool allPrevStagesDone = await _db.Stages
                .Where(s => s.BuildingId == ss.Stage.BuildingId && s.OrderIndex < ss.Stage.OrderIndex)
                .AllAsync(s => s.Status == WorkStatus.Finished || s.Status == WorkStatus.Paid);

            if (!allPrevStagesDone)
            {
                MessageBox.Show(
                    ResourceHelper.GetString("OperationsView_PreviousStagesRequired", "You must finish all previous stages before starting this stage."),
                    ResourceHelper.GetString("OperationsView_RuleOrderTitle", "Order rule"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        // State checks
        if (ss.Status == WorkStatus.Finished || ss.Status == WorkStatus.Paid || ss.Status == WorkStatus.Stopped)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SubStageCannotStart", "This sub-stage cannot be started in its current state."),
                ResourceHelper.GetString("OperationsView_RuleTitle", "Rule"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // ✅ Start sub-stage
        ss.Status = WorkStatus.Ongoing;
        if (ss.StartDate == null) ss.StartDate = DateTime.Today;

        // Bubble up to stage/building
        if (ss.Stage.Status == WorkStatus.NotStarted)
        {
            ss.Stage.Status = WorkStatus.Ongoing;
            if (ss.Stage.StartDate == null) ss.Stage.StartDate = DateTime.Today;
        }
        if (ss.Stage.Building.Status == WorkStatus.NotStarted)
        {
            ss.Stage.Building.Status = WorkStatus.Ongoing;
        }

        await _db.SaveChangesAsync();

        await ReloadBuildingsAsync(ss.Stage.BuildingId, ss.StageId);
        await ReloadStagesAndSubStagesAsync(ss.Stage.BuildingId, ss.StageId);
    }

    private async void FinishSubStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        var sel = SubStagesGrid.SelectedItem as SubStage;
        if (sel == null) return;

        var ss = await _db.SubStages
            .Include(x => x.MaterialUsages)
            .Include(x => x.Stage)
                .ThenInclude(s => s.SubStages)
            .Include(x => x.Stage.Building)
                .ThenInclude(b => b.Stages)
            .FirstAsync(x => x.Id == sel.Id);

        // ✅ Only Ongoing can be finished
        if (ss.Status != WorkStatus.Ongoing)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_OnlyOngoingCanFinish", "You can only finish an Ongoing sub-stage."),
                ResourceHelper.GetString("OperationsView_RuleTitle", "Rule"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ss.Status = WorkStatus.Finished;
        if (ss.EndDate == null) ss.EndDate = DateTime.Today;

        var freezeDate = ss.EndDate.Value.Date;
        foreach (var usage in ss.MaterialUsages)
        {
            usage.UsageDate = freezeDate;
        }

        // Recompute parents (also sets dates)
        UpdateStageStatusFromSubStages(ss.Stage);
        UpdateBuildingStatusFromStages(ss.Stage.Building);

        await _db.SaveChangesAsync();

        await ReloadBuildingsAsync(ss.Stage.BuildingId, ss.StageId);
        await ReloadStagesAndSubStagesAsync(ss.Stage.BuildingId, ss.StageId);
    }

    private async void ResetSubStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        var sel = SubStagesGrid.SelectedItem as SubStage;
        if (sel == null) return;

        var ss = await _db.SubStages
            .Include(x => x.Stage)
                .ThenInclude(s => s.SubStages)
            .Include(x => x.Stage.Building)
                .ThenInclude(b => b.Stages)
            .FirstAsync(x => x.Id == sel.Id);

        if (ss.Status != WorkStatus.Ongoing)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_OnlyOngoingCanReset", "Only an Ongoing sub-stage can be reset to Not Started."),
                ResourceHelper.GetString("OperationsView_RuleTitle", "Rule"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ss.Status = WorkStatus.NotStarted;
        ss.StartDate = null;
        ss.EndDate = null;

        UpdateStageStatusFromSubStages(ss.Stage);
        UpdateBuildingStatusFromStages(ss.Stage.Building);

        await _db.SaveChangesAsync();

        await ReloadBuildingsAsync(ss.Stage.BuildingId, ss.StageId);
        await ReloadStagesAndSubStagesAsync(ss.Stage.BuildingId, ss.StageId);
    }

    private async Task ReloadStagesAndSubStagesAsync(int buildingId, int? preferredStageId = null)
    {
        // Reload stages for buildingId
        var stageEntities = await _db.Stages
            .Where(s => s.BuildingId == buildingId)
            .OrderBy(s => s.OrderIndex)
            .Include(s => s.SubStages)
            .AsNoTracking()
            .ToListAsync();

        var stageRows = stageEntities
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Status,
                ProgressPercent = ComputeStageProgress(s),
                OngoingSubStageName = ComputeCurrentSubStageName(s)
            })
            .ToList();

        StagesGrid.ItemsSource = stageRows;

        // Select preferred stage (or first)
        int stageId = preferredStageId ?? stageRows.FirstOrDefault()?.Id ?? 0;

        if (stageId == 0)
        {
            StagesGrid.SelectedItem = null;
            SubStagesGrid.ItemsSource = null;
            MaterialsGrid.ItemsSource = null;
            _currentStageId = null;
            UpdateSubStageLaborTotal(null);
            UpdateMaterialsTotal(null);
            return;
        }

        var stageToSelect = stageRows.FirstOrDefault(s => s.Id == stageId);
        if (stageToSelect != null)
        {
            StagesGrid.SelectedItem = stageToSelect;
            StagesGrid.ScrollIntoView(stageToSelect);
        }
        _currentStageId = stageId;

        // Load tracked sub-stages for that stage
        var subs = await _db.SubStages
            .Where(ss => ss.StageId == stageId)
            .OrderBy(ss => ss.OrderIndex)
            .ToListAsync();

        SubStagesGrid.ItemsSource = subs;
        UpdateSubStageLaborTotal(subs);

        // Auto-select first sub-stage (if any) and load its materials
        var firstSub = subs.FirstOrDefault();
        if (firstSub != null)
        {
            SubStagesGrid.SelectedItem = firstSub;
            SubStagesGrid.ScrollIntoView(firstSub);

            await LoadMaterialsForSubStageAsync(firstSub);
        }
        else
        {
            MaterialsGrid.ItemsSource = null;
            UpdateMaterialsTotal(null);
        }
    }

    private static void UpdateStageStatusFromSubStages(Stage? s)
    {
        if (s == null)
        {
            return;
        }

        if (s.SubStages == null || s.SubStages.Count == 0)
        {
            s.Status = WorkStatus.NotStarted;
            s.StartDate = null;
            s.EndDate = null;
            return;
        }

        bool allPaid = s.SubStages.All(ss => ss.Status == WorkStatus.Paid);
        bool anyOngoing = s.SubStages.Any(ss => ss.Status == WorkStatus.Ongoing);
        bool allNotStarted = s.SubStages.All(ss => ss.Status == WorkStatus.NotStarted);
        bool allDoneLike = s.SubStages.All(ss => ss.Status == WorkStatus.Finished || ss.Status == WorkStatus.Paid);

        var prev = s.Status;

        if (allPaid)
            s.Status = WorkStatus.Paid;
        else if (anyOngoing)
            s.Status = WorkStatus.Ongoing;
        else if (allNotStarted)
            s.Status = WorkStatus.NotStarted;
        else if (allDoneLike)
            s.Status = WorkStatus.Finished;
        // else: mixed — leave as-is, or set to Ongoing if you prefer

        // Dates
        if (s.Status == WorkStatus.Ongoing && s.StartDate == null)
            s.StartDate = DateTime.Today;

        if ((s.Status == WorkStatus.Finished || s.Status == WorkStatus.Paid) && s.EndDate == null)
            s.EndDate = DateTime.Today;

        if (s.Status == WorkStatus.NotStarted)
        {
            s.StartDate = null;
            s.EndDate = null;
        }
    }

    private static void UpdateBuildingStatusFromStages(Building? b)
    {
        if (b == null || b.Stages == null || b.Stages.Count == 0) return;

        bool allPaid = b.Stages.All(st => st.Status == WorkStatus.Paid);
        bool anyOngoing = b.Stages.Any(st => st.Status == WorkStatus.Ongoing);
        bool allNotStarted = b.Stages.All(st => st.Status == WorkStatus.NotStarted);
        bool allDoneLike = b.Stages.All(st => st.Status == WorkStatus.Finished || st.Status == WorkStatus.Paid);

        var prev = b.Status;

        if (allPaid)
            b.Status = WorkStatus.Paid;
        else if (anyOngoing)
            b.Status = WorkStatus.Ongoing;
        else if (allNotStarted)
            b.Status = WorkStatus.NotStarted;
        else if (allDoneLike)
            b.Status = WorkStatus.Finished;
        // else: mixed — leave as-is or set Ongoing if you prefer
    }

    private async void AddMaterialToSubStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null)
            return;

        // We require a selected SubStage (you’re already binding real SubStage entities)
        var ss = SubStagesGrid.SelectedItem as SubStage;
        if (ss == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectSubStageFirst", "Select a sub-stage first."),
                ResourceHelper.GetString("OperationsView_NoSubStageTitle", "No sub-stage"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Open your dialog (filters active materials not already used by this sub-stage)
        var dlg = new Kanstraction.Views.AddMaterialToSubStageDialog(_db, ss.Id)
        {
            Owner = Window.GetWindow(this)
        };

        if (dlg.ShowDialog() != true)
            return;

        // Defensive checks (dialog already validates)
        if (dlg.MaterialId == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectMaterialPrompt", "Please select a material."),
                ResourceHelper.GetString("Common_RequiredTitle", "Required"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Create a MaterialUsage row (instance-only, does not touch presets)
        var mu = new MaterialUsage
        {
            SubStageId = ss.Id,
            MaterialId = dlg.MaterialId.Value,
            Qty = dlg.Qty,
            UsageDate = DateTime.Today,
            Notes = null
        };

        try
        {
            _db.MaterialUsages.Add(mu);
            await _db.SaveChangesAsync();

            // Refresh the materials list for this sub-stage
            await LoadMaterialsForSubStageAsync(ss);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_AddMaterialFailedFormat", "Failed to add material:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void DeleteMaterialUsage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null)
            return;

        if (sender is not Button btn)
            return;

        if (btn.Tag is not int usageId)
            return;

        var usage = await _db.MaterialUsages
            .Include(mu => mu.Material)
            .Include(mu => mu.SubStage)
            .FirstOrDefaultAsync(mu => mu.Id == usageId);

        if (usage == null)
            return;

        var confirm = MessageBox.Show(
            string.Format(ResourceHelper.GetString("OperationsView_DeleteMaterialConfirmFormat", "Delete material '{0}'?"), usage.Material.Name),
            ResourceHelper.GetString("OperationsView_DeleteMaterialTitle", "Delete material"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            _db.MaterialUsages.Remove(usage);
            await _db.SaveChangesAsync();

            _editingMaterialUsage = null;
            _originalMaterialQuantity = null;

            if (usage.SubStage != null)
            {
                await LoadMaterialsForSubStageAsync(usage.SubStage);
            }
            else
            {
                var subStage = await _db.SubStages.FirstOrDefaultAsync(ss => ss.Id == usage.SubStageId);
                if (subStage != null)
                {
                    await LoadMaterialsForSubStageAsync(subStage);
                }
                else
                {
                    MaterialsGrid.ItemsSource = null;
                    UpdateMaterialsTotal(null);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_DeleteMaterialFailedFormat", "Failed to delete material:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void AddSubStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        // Identify selected Stage (entity preferred)
        int stageId;
        var stageEntity = StagesGrid.SelectedItem as Stage;
        if (stageEntity != null)
        {
            stageId = stageEntity.Id;
        }
        else
        {
            // fallback if the grid is bound to a projection
            var dyn = StagesGrid.SelectedItem as dynamic;
            if (dyn == null) return;
            stageId = (int)dyn.Id;
        }

        // Count current sub-stages to suggest max order
        var currentCount = await _db.SubStages.CountAsync(x => x.StageId == stageId);

        // Open dialog
        var dlg = new Kanstraction.Views.AddSubStageDialog(currentCount)
        {
            Owner = Window.GetWindow(this)
        };

        if (dlg.ShowDialog() != true) return;

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Clamp order again server-side: 1..currentCount+1
            var desiredOrder = Math.Max(1, Math.Min(dlg.OrderIndex, currentCount + 1));

            // Shift existing ≥ desiredOrder
            var toShift = await _db.SubStages
                .Where(ss => ss.StageId == stageId && ss.OrderIndex >= desiredOrder)
                .OrderBy(ss => ss.OrderIndex)
                .ToListAsync();

            foreach (var s in toShift)
                s.OrderIndex += 1;

            // Create new sub-stage (instance-only)
            var newSub = new SubStage
            {
                StageId = stageId,
                Name = dlg.SubStageName,
                OrderIndex = desiredOrder,
                LaborCost = dlg.LaborCost,
                Status = WorkStatus.NotStarted
            };
            _db.SubStages.Add(newSub);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            // Reload sub-stages for this stage and select the new one
            var subs = await _db.SubStages
                .Where(ss => ss.StageId == stageId)
                .OrderBy(ss => ss.OrderIndex)
                .ToListAsync();

            SubStagesGrid.ItemsSource = subs;
            UpdateSubStageLaborTotal(subs);

            var select = subs.FirstOrDefault(x => x.Id == newSub.Id);
            if (select != null)
            {
                SubStagesGrid.SelectedItem = select;
                SubStagesGrid.ScrollIntoView(select);
            }

            // Optionally clear materials and wait for user to add
            MaterialsGrid.ItemsSource = null;
            UpdateMaterialsTotal(null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_AddSubStageFailedFormat", "Failed to add sub-stage:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void DeleteSubStageInstance_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        if (sender is not Button btn) return;

        if (btn.Tag is not int subStageId) return;

        var subStage = await _db.SubStages
            .Include(ss => ss.Stage)
                .ThenInclude(st => st.SubStages)
            .Include(ss => ss.Stage.Building)
                .ThenInclude(b => b.Stages)
            .FirstOrDefaultAsync(ss => ss.Id == subStageId);

        if (subStage == null) return;

        var confirm = MessageBox.Show(
            string.Format(ResourceHelper.GetString("OperationsView_DeleteSubStageConfirmFormat", "Delete sub-stage '{0}'?"), subStage.Name),
            ResourceHelper.GetString("OperationsView_DeleteSubStageTitle", "Delete sub-stage"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var stage = subStage.Stage;
            if (stage == null)
            {
                stage = await _db.Stages
                    .Include(st => st.SubStages)
                    .FirstOrDefaultAsync(st => st.Id == subStage.StageId);

                if (stage == null)
                {
                    throw new InvalidOperationException($"Stage {subStage.StageId} could not be loaded for sub-stage {subStage.Id}.");
                }

                subStage.Stage = stage;
            }

            var building = stage.Building;
            if (building == null)
            {
                building = await _db.Buildings
                    .Include(b => b.Stages)
                    .FirstOrDefaultAsync(b => b.Id == stage.BuildingId);

                if (building != null)
                {
                    stage.Building = building;
                }
            }

            int stageId = stage.Id;
            int buildingId = stage.BuildingId;
            int removedOrder = subStage.OrderIndex;

            var siblingsToShift = stage.SubStages?
                .Where(ss => ss.Id != subStage.Id && ss.OrderIndex > removedOrder)
                .ToList();

            if (siblingsToShift != null)
            {
                foreach (var other in siblingsToShift)
                {
                    other.OrderIndex -= 1;
                }
            }

            stage.SubStages?.Remove(subStage);
            _db.SubStages.Remove(subStage);

            UpdateStageStatusFromSubStages(stage);
            if (building != null)
            {
                UpdateBuildingStatusFromStages(building);
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await ReloadStagesAndSubStagesAsync(buildingId, stageId);
            await ReloadBuildingsAsync(buildingId, stageId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_DeleteSubStageFailedFormat", "Failed to delete sub-stage:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ResolvePayment_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null || _currentProject == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectProjectFirst", "Select a project first."),
                ResourceHelper.GetString("OperationsView_NoProjectTitle", "No project"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // 1) Gather eligible sub-stages: Finished (not Paid/Stopped) for current project
        var eligible = await _db.SubStages
            .Where(ss => ss.Stage.Building.ProjectId == _currentProject.Id
                      && ss.Status == WorkStatus.Finished)
            .Include(ss => ss.Stage)
                .ThenInclude(st => st.Building)
            .OrderBy(ss => ss.Stage.Building.Code)
            .ThenBy(ss => ss.Stage.OrderIndex)
            .ThenBy(ss => ss.OrderIndex)
            .ToListAsync();

        if (eligible.Count == 0)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_NoCompletedSubStages", "No completed sub-stages to resolve."),
                ResourceHelper.GetString("OperationsView_NothingToDoTitle", "Nothing to do"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // 2) Build report DTO (grouped) for PDF
        var data = new PaymentReportRenderer.ReportData
        {
            ProjectName = _currentProject.Name,
            GeneratedAt = DateTime.Now
        };

        var byBuilding = eligible.GroupBy(ss => ss.Stage.Building);
        foreach (var bgroup in byBuilding)
        {
            var b = bgroup.Key; // Building
            var bDto = new PaymentReportRenderer.BuildingGroup { BuildingCode = b.Code };

            var byStage = bgroup.GroupBy(ss => ss.Stage)
                                .OrderBy(g => g.Key.OrderIndex);

            foreach (var sgroup in byStage)
            {
                var st = sgroup.Key; // Stage
                var sDto = new PaymentReportRenderer.StageGroup { StageName = st.Name };

                foreach (var ss in sgroup.OrderBy(x => x.OrderIndex))
                {
                    sDto.Items.Add(new PaymentReportRenderer.SubStageRow
                    {
                        SubStageName = ss.Name,
                        StageName = st.Name,
                        LaborCost = ss.LaborCost
                    });
                }

                sDto.StageSubtotal = sDto.Items.Sum(i => i.LaborCost);
                bDto.Stages.Add(sDto);
            }

            bDto.BuildingSubtotal = bDto.Stages.Sum(s => s.StageSubtotal);
            data.Buildings.Add(bDto);
        }

        data.GrandTotal = data.Buildings.Sum(b => b.BuildingSubtotal);

        // 3) Ask where to save
        var sfd = new SaveFileDialog
        {
            Title = ResourceHelper.GetString("OperationsView_SavePaymentReportTitle", "Save payment resolution report"),
            Filter = "PDF files (*.pdf)|*.pdf",
            FileName = $"Payment_{_currentProject.Name}_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
        };
        if (sfd.ShowDialog(Window.GetWindow(this)) != true)
            return;

        // 4) Render PDF (temp → final inside renderer)
        try
        {
            var renderer = new PaymentReportRenderer();
            renderer.Render(data, sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_GeneratePdfFailedFormat", "Failed to generate PDF:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // 5) On success, mark as Paid and cascade (transaction)
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Mark each eligible sub-stage as Paid
            foreach (var ss in eligible)
            {
                ss.Status = WorkStatus.Paid;
                // keep EndDate as is (already set when finished)
            }

            // Cascade: update each involved Stage and Building
            // We already have Stage and Building loaded for these sub-stages
            var affectedStages = eligible.Select(e1 => e1.Stage).Distinct().ToList();
            foreach (var st in affectedStages)
            {
                // Make sure st.SubStages is loaded (it is, because eligible share the same context; but ensure anyway)
                await _db.Entry(st).Collection(x => x.SubStages).LoadAsync();
                UpdateStageStatusFromSubStages(st); // your existing helper
            }

            var affectedBuildings = eligible.Select(e1 => e1.Stage.Building).Distinct().ToList();
            foreach (var b in affectedBuildings)
            {
                await _db.Entry(b).Collection(x => x.Stages).LoadAsync();
                UpdateBuildingStatusFromStages(b);  // your existing helper
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_MarkPaidFailedFormat", "Failed to mark items as paid after generating the PDF:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // 6) Refresh UI
        await ReloadBuildingsAsync();
        MessageBox.Show(
            ResourceHelper.GetString("OperationsView_PaymentResolutionCompleted", "Payment resolution completed."),
            ResourceHelper.GetString("OperationsView_PaymentResolutionCompletedTitle", "Done"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // Small DTO matching the dialog's PresetVm
    private sealed class StagePresetPick
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private async void DeleteStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        if (sender is not Button btn) return;

        if (btn.Tag is not int stageId) return;

        var stage = await _db.Stages
            .Include(st => st.SubStages)
            .Include(st => st.Building)
                .ThenInclude(b => b.Stages)
                    .ThenInclude(s => s.SubStages)
            .FirstOrDefaultAsync(st => st.Id == stageId);

        if (stage == null) return;

        var confirm = MessageBox.Show(
            string.Format(ResourceHelper.GetString("OperationsView_DeleteStageConfirmFormat", "Delete stage '{0}'?"), stage.Name),
            ResourceHelper.GetString("OperationsView_DeleteStageTitle", "Delete stage"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            int buildingId = stage.BuildingId;
            int removedOrder = stage.OrderIndex;

            foreach (var other in stage.Building.Stages.Where(s => s.Id != stage.Id && s.OrderIndex > removedOrder))
            {
                other.OrderIndex -= 1;
            }

            stage.Building.Stages.Remove(stage);
            _db.Stages.Remove(stage);

            UpdateBuildingStatusFromStages(stage.Building);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await ReloadStagesAndSubStagesAsync(buildingId);
            await ReloadBuildingsAsync(buildingId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_DeleteStageFailedFormat", "Failed to delete stage:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void AddStageToBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        // We need a selected building (your BuildingsGrid binds to an anonymous projection)
        var bRow = BuildingsGrid.SelectedItem as dynamic;
        if (bRow == null) return;

        int buildingId = (int)bRow.Id;

        var buildingTypeId = await _db.Buildings
            .Where(b => b.Id == buildingId)
            .Select(b => b.BuildingTypeId)
            .FirstAsync();

        // Load current building stages (names + count)
        var currentStages = await _db.Stages
            .Where(s => s.BuildingId == buildingId)
            .OrderBy(s => s.OrderIndex)
            .Select(s => new { s.Id, s.Name, s.OrderIndex })
            .ToListAsync();

        var existingNames = currentStages.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int currentCount = currentStages.Count;

        // Available presets = active StagePresets NOT already present by name in this building
        var availablePresets = await _db.StagePresets
            .Where(p => p.IsActive && !existingNames.Contains(p.Name))
            .OrderBy(p => p.Name)
            .Select(p => new StagePresetPick { Id = p.Id, Name = p.Name })
            .ToListAsync();

        if (availablePresets.Count == 0)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_NoPresetToAddMessage", "All active stage presets are already in this building."),
                ResourceHelper.GetString("OperationsView_NoPresetTitle", "No preset"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Open dialog
        var dlg = new Kanstraction.Views.AddStageToBuildingDialog(
            availablePresets.ConvertAll(p => new Kanstraction.Views.AddStageToBuildingDialog.PresetVm { Id = p.Id, Name = p.Name }),
            currentCount)
        {
            Owner = Window.GetWindow(this)
        };

        if (dlg.ShowDialog() != true) return;

        int presetId = dlg.SelectedPresetId!.Value;
        int desiredOrder = dlg.OrderIndex; // already clamped by dialog

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Shift existing stages with OrderIndex >= desiredOrder
            var toShift = await _db.Stages
                .Where(s => s.BuildingId == buildingId && s.OrderIndex >= desiredOrder)
                .OrderBy(s => s.OrderIndex)
                .ToListAsync();

            foreach (var s in toShift)
                s.OrderIndex += 1;

            // Resolve the preset's name
            var presetName = await _db.StagePresets
                .Where(p => p.Id == presetId)
                .Select(p => p.Name)
                .FirstAsync();

            // Create the new Stage (instance-only)
            var newStage = new Stage
            {
                BuildingId = buildingId,
                Name = presetName,
                OrderIndex = desiredOrder,
                Status = WorkStatus.NotStarted
            };
            _db.Stages.Add(newStage);
            await _db.SaveChangesAsync(); // need newStage.Id for children

            // Clone SubStagePresets (+ material usage presets) into instance
            var subPresets = await _db.SubStagePresets
                .Where(sp => sp.StagePresetId == presetId)
                .OrderBy(sp => sp.OrderIndex)
                .ToListAsync();

            var subPresetIds = subPresets.ConvertAll(sp => sp.Id);

            var laborLookup = await _db.BuildingTypeSubStageLabors
                .Where(x => x.BuildingTypeId == buildingTypeId && subPresetIds.Contains(x.SubStagePresetId) && x.LaborCost.HasValue)
                .ToDictionaryAsync(x => x.SubStagePresetId, x => x.LaborCost!.Value);

            var materialUsageLookup = await _db.BuildingTypeMaterialUsages
                .Where(x => x.BuildingTypeId == buildingTypeId && subPresetIds.Contains(x.SubStagePresetId) && x.Qty.HasValue)
                .ToDictionaryAsync(x => (x.SubStagePresetId, x.MaterialId), x => x.Qty!.Value);

            foreach (var sp in subPresets)
            {
                var sub = new SubStage
                {
                    StageId = newStage.Id,
                    Name = sp.Name,
                    OrderIndex = sp.OrderIndex,
                    Status = WorkStatus.NotStarted,
                    LaborCost = laborLookup.TryGetValue(sp.Id, out var labor)
                        ? labor
                        : 0m
                };
                _db.SubStages.Add(sub);
                await _db.SaveChangesAsync(); // need sub.Id to attach usages

                var muPresets = await _db.MaterialUsagesPreset
                    .Where(mup => mup.SubStagePresetId == sp.Id)
                    .ToListAsync();

                foreach (var mup in muPresets)
                {
                    var qty = materialUsageLookup.TryGetValue((sp.Id, mup.MaterialId), out var buildingTypeQty)
                        ? buildingTypeQty
                        : 0m;

                    var mu = new MaterialUsage
                    {
                        SubStageId = sub.Id,
                        MaterialId = mup.MaterialId,
                        Qty = qty,
                        UsageDate = DateTime.Today,
                        Notes = null
                    };
                    _db.MaterialUsages.Add(mu);
                }
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            // Refresh UI: reload stages for this building and select the new stage
            await ReloadStagesAndSubStagesAsync(buildingId, newStage.Id);
            // Also refresh buildings row (progress / current stage)
            await ReloadBuildingsAsync(buildingId, newStage.Id);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_AddStageFailedFormat", "Failed to add stage:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

}