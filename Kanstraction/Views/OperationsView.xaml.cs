using Kanstraction.Data;
using Kanstraction.Entities;
using Kanstraction.Services;
using System.Windows;
using System.Windows.Controls;
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
            new PropertyMetadata("Select a project"));

    private int? _currentBuildingId;
    private int? _currentStageId;

    public OperationsView()
    {
        InitializeComponent();
    }

    public void SetDb(AppDbContext db) => _db = db;

    public async Task ShowProject(Project p)
    {
        if (_db == null) return;

        _currentProject = p;
        Breadcrumb = $"Project: {p.Name}";

        // Clear right-side panels & selection state before loading
        StagesGrid.ItemsSource = null;
        SubStagesGrid.ItemsSource = null;
        MaterialsGrid.ItemsSource = null;
        _currentBuildingId = null;
        _currentStageId = null;

        // One source of truth for loading/selection of buildings
        await ReloadBuildingsAsync();  // (no specific selection; will select first if none)
    }

    private async void BuildingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_db == null) return;
        dynamic? b = BuildingsGrid.SelectedItem;
        if (b == null) return;

        _currentBuildingId = (int)b.Id;

        await ReloadStagesAndSubStagesAsync((int)b.Id);

        Breadcrumb = UpdateBreadcrumbWithBuilding(b.Code);
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
        var firstSub = subStages.FirstOrDefault();
        if (firstSub != null)
        {
            SubStagesGrid.SelectedItem = firstSub;
            SubStagesGrid.ScrollIntoView(firstSub);

            var usages = await _db.MaterialUsages
                .Include(mu => mu.Material)
                .Where(mu => mu.SubStageId == firstSub.Id)
                .OrderBy(mu => mu.Material.Name)
                .ToListAsync();

            MaterialsGrid.ItemsSource = usages;
        }
        else
        {
            MaterialsGrid.ItemsSource = null;
        }
    }

    private async void SubStagesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_db == null) return;
        var ss = SubStagesGrid.SelectedItem as SubStage;
        if (ss == null)
        {
            MaterialsGrid.ItemsSource = null;
            return;
        }

        // Load tracked usages + material (no AsNoTracking so edits persist)
        var usages = await _db.MaterialUsages
            .Include(mu => mu.Material)
            .Where(mu => mu.SubStageId == ss.Id)
            .OrderBy(mu => mu.Material.Name)
            .ToListAsync();

        MaterialsGrid.ItemsSource = usages;
    }
    private async void SubStagesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_db == null) return;
        if (e.EditAction != DataGridEditAction.Commit) return;

        if (e.Column is DataGridTextColumn col &&
            (col.Header?.ToString()?.Equals("Labor", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            var ss = e.Row.Item as SubStage;   // now valid
            if (ss == null) return;

            if (ss.LaborCost < 0)
            {
                MessageBox.Show("Le coût de main-d'œuvre ne peut pas être négatif.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                ss.LaborCost = 0;
            }

            await _db.SaveChangesAsync();
        }
    }
    private async void MaterialsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_db == null) return;
        if (e.EditAction != DataGridEditAction.Commit) return;

        if (e.Row.Item is MaterialUsage mu)
        {
            // Validate: non-negative
            if (mu.Qty < 0)
            {
                MessageBox.Show("La quantité ne peut pas être négative.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                mu.Qty = 0;
            }

            await _db.SaveChangesAsync();
            // If you show any totals elsewhere, refresh them here
        }
    }
    private string UpdateBreadcrumbWithBuilding(string code)
    {
        var baseText = Breadcrumb;
        var idx = baseText.IndexOf(" > Building", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) baseText = baseText.Substring(0, idx);
        return $"{baseText} > Building {code}";
    }

    private async void AddBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null)
        {
            MessageBox.Show("Sélectionnez d'abord un projet.", "Aucun projet", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("Un bâtiment avec ce code existe déjà dans ce projet.", "Code dupliqué",
                MessageBoxButton.OK, MessageBoxImage.Warning);
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
                        LaborCost = sp.LaborCost
                    };
                    _db.SubStages.Add(sub);

                    // Copy MATERIALS from MaterialUsagePreset → MaterialUsage
                    var muPresets = await _db.MaterialUsagesPreset
                        .Where(mu => mu.SubStagePresetId == sp.Id)
                        .ToListAsync();

                    foreach (var mup in muPresets)
                    {
                        // Seed with today's date; you can edit later
                        var mu = new MaterialUsage
                        {
                            SubStage = sub,                  // link via navigation
                            MaterialId = mup.MaterialId,
                            Qty = mup.Qty,
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
            MessageBox.Show("Échec de création du bâtiment :\n" + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ReloadBuildingsAsync(int? selectBuildingId = null)
    {
        if (_currentProject == null) return;

        // Load everything we need to compute the view fields
        var buildings = await _db.Buildings
            .Where(b => b.ProjectId == _currentProject.Id)
            .Include(b => b.BuildingType)
            .Include(b => b.Stages)
                .ThenInclude(s => s.SubStages)
            .AsNoTracking()
            .ToListAsync();

        // Map to the SAME property names your grid binds to
        var data = buildings
            .Select(b => new
            {
                b.Id,
                b.Code,
                TypeName = b.BuildingType?.Name ?? "",
                Status = b.Status,                       // your converter can handle string or enum name
                ProgressPercent = ComputeBuildingProgress(b),       // 0..100 decimal/int
                CurrentStageName = ComputeCurrentStageName(b)       // string
            })
            .OrderBy(x => x.Id)
            .ToList();

        BuildingsGrid.ItemsSource = data;

        // Keep (or set) selection on the newly created building if provided
        if (selectBuildingId.HasValue)
        {
            var newly = data.FirstOrDefault(x => x.Id == selectBuildingId.Value);
            if (newly != null)
            {
                BuildingsGrid.SelectedItem = newly;
                BuildingsGrid.ScrollIntoView(newly);
            }
            else if (data.Count > 0 && BuildingsGrid.SelectedItem == null)
            {
                BuildingsGrid.SelectedIndex = 0;
            }
        }
        else if (data.Count > 0 && BuildingsGrid.SelectedItem == null)
        {
            BuildingsGrid.SelectedIndex = 0;
        }
    }

    private static int ComputeBuildingProgress(Building b)
    {
        // Your rule: NotStarted/Ongoing = 0; Finished/Paid/Stopped = 1
        static decimal StatusVal(WorkStatus s) =>
            (s == WorkStatus.Finished || s == WorkStatus.Paid || s == WorkStatus.Stopped) ? 1m : 0m;

        if (b.Stages == null || b.Stages.Count == 0)
            return (int)(StatusVal(b.Status) * 100m);

        decimal perStageWeight = 1m / b.Stages.Count;
        decimal sum = 0m;

        foreach (var s in b.Stages.OrderBy(s => s.OrderIndex))
        {
            if (s.SubStages == null || s.SubStages.Count == 0)
            {
                sum += perStageWeight * StatusVal(s.Status);
                continue;
            }

            decimal perSub = 1m / s.SubStages.Count;
            decimal stageSum = 0m;

            foreach (var ss in s.SubStages.OrderBy(ss => ss.OrderIndex))
                stageSum += perSub * StatusVal(ss.Status);

            sum += perStageWeight * stageSum;
        }
        return (int)Math.Round(sum * 100m, MidpointRounding.AwayFromZero);
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
                        LaborCost = ssp.LaborCost
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

        var confirm = MessageBox.Show("Arrêter ce bâtiment ? Toutes les étapes et sous-étapes non terminées/payées seront marquées Arrêtées.",
            "Confirmer l'arrêt", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
            MessageBox.Show("Échec de l'arrêt du bâtiment :\n" + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
            MessageBox.Show("Une autre sous-étape est déjà en cours dans ce bâtiment. Terminez-la d'abord.",
                "Règle", MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show("Vous devez terminer la sous-étape précédente d'abord.", "Règle d'ordre",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show("Vous devez terminer toutes les étapes précédentes avant de commencer cette étape.", "Règle d'ordre",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // State checks
        if (ss.Status == WorkStatus.Finished || ss.Status == WorkStatus.Paid || ss.Status == WorkStatus.Stopped)
        {
            MessageBox.Show("Cette sous-étape ne peut pas être démarrée dans son état actuel.", "Règle",
                MessageBoxButton.OK, MessageBoxImage.Warning);
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

        await ReloadBuildingsAsync(ss.Stage.BuildingId);
        await ReloadStagesAndSubStagesAsync(ss.Stage.BuildingId, ss.StageId);
    }

    private async void FinishSubStage_Click(object sender, RoutedEventArgs e)
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

        // ✅ Only Ongoing can be finished
        if (ss.Status != WorkStatus.Ongoing)
        {
            MessageBox.Show("Vous ne pouvez terminer qu'une sous-étape En cours.", "Règle",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ss.Status = WorkStatus.Finished;
        if (ss.EndDate == null) ss.EndDate = DateTime.Today;

        // Recompute parents (also sets dates)
        UpdateStageStatusFromSubStages(ss.Stage);
        UpdateBuildingStatusFromStages(ss.Stage.Building);

        await _db.SaveChangesAsync();

        await ReloadBuildingsAsync(ss.Stage.BuildingId);
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
            MessageBox.Show("Seule une sous-étape En cours peut être réinitialisée à Non commencée.", "Règle",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ss.Status = WorkStatus.NotStarted;
        ss.StartDate = null;
        ss.EndDate = null;

        UpdateStageStatusFromSubStages(ss.Stage);
        UpdateBuildingStatusFromStages(ss.Stage.Building);

        await _db.SaveChangesAsync();

        await ReloadBuildingsAsync(ss.Stage.BuildingId);
        await ReloadStagesAndSubStagesAsync(ss.Stage.BuildingId, ss.StageId);
    }

    private async Task ReloadStagesAndSubStagesAsync(int buildingId, int? preferredStageId = null)
    {
        // Reload stages for buildingId
        var stages = await _db.Stages
            .Where(s => s.BuildingId == buildingId)
            .OrderBy(s => s.OrderIndex)
            .AsNoTracking()
            .ToListAsync();
        StagesGrid.ItemsSource = stages;

        // Select preferred stage (or first)
        int stageId = preferredStageId ?? stages.FirstOrDefault()?.Id ?? 0;

        if (stageId == 0)
        {
            SubStagesGrid.ItemsSource = null;
            MaterialsGrid.ItemsSource = null;
            _currentStageId = null;
            return;
        }

        var stageToSelect = stages.FirstOrDefault(s => s.Id == stageId);
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

        // Auto-select first sub-stage (if any) and load its materials
        var firstSub = subs.FirstOrDefault();
        if (firstSub != null)
        {
            SubStagesGrid.SelectedItem = firstSub;
            SubStagesGrid.ScrollIntoView(firstSub);

            var usages = await _db.MaterialUsages
                .Include(mu => mu.Material)
                .Where(mu => mu.SubStageId == firstSub.Id)
                .OrderBy(mu => mu.Material.Name)
                .ToListAsync();

            MaterialsGrid.ItemsSource = usages;
        }
        else
        {
            MaterialsGrid.ItemsSource = null;
        }
    }

    private static void UpdateStageStatusFromSubStages(Stage s)
    {
        if (s.SubStages == null || s.SubStages.Count == 0) return;

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

    private static void UpdateBuildingStatusFromStages(Building b)
    {
        if (b.Stages == null || b.Stages.Count == 0) return;

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
            MessageBox.Show("Sélectionnez d'abord une sous-étape.", "Aucune sous-étape", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show("Veuillez sélectionner un matériau.", "Obligatoire", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            var usages = await _db.MaterialUsages
                .Include(x => x.Material)
                .Where(x => x.SubStageId == ss.Id)
                .OrderBy(x => x.Material.Name)
                .ToListAsync();

            MaterialsGrid.ItemsSource = usages;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Échec de l'ajout du matériau :\n" + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
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

            var select = subs.FirstOrDefault(x => x.Id == newSub.Id);
            if (select != null)
            {
                SubStagesGrid.SelectedItem = select;
                SubStagesGrid.ScrollIntoView(select);
            }

            // Optionally clear materials and wait for user to add
            MaterialsGrid.ItemsSource = null;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show("Échec de l'ajout de la sous-étape :\n" + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ResolvePayment_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null || _currentProject == null)
        {
            MessageBox.Show("Sélectionnez d'abord un projet.", "Aucun projet", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show("Aucune sous-étape terminée à résoudre.", "Rien à faire",
                MessageBoxButton.OK, MessageBoxImage.Information);
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
            Title = "Save payment resolution report",
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
            MessageBox.Show("Échec de génération du PDF :\n" + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 5) On success, mark as Paid and cascade (transaction)
        /*using var tx = await _db.Database.BeginTransactionAsync();
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
            MessageBox.Show("Échec du marquage des éléments comme payés après la génération du PDF :\n" + ex.Message,
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }*/

        // 6) Refresh UI
        await ReloadBuildingsAsync();
        MessageBox.Show("Résolution de paiement terminée.", "Terminé",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // Small DTO matching the dialog's PresetVm
    private sealed class StagePresetPick
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private async void AddStageToBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        // We need a selected building (your BuildingsGrid binds to an anonymous projection)
        var bRow = BuildingsGrid.SelectedItem as dynamic;
        if (bRow == null) return;

        int buildingId = (int)bRow.Id;

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
            MessageBox.Show("Tous les préréglages d'étape actifs sont déjà dans ce bâtiment.", "Aucun préréglage",
                MessageBoxButton.OK, MessageBoxImage.Information);
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

            foreach (var sp in subPresets)
            {
                var sub = new SubStage
                {
                    StageId = newStage.Id,
                    Name = sp.Name,
                    OrderIndex = sp.OrderIndex,
                    Status = WorkStatus.NotStarted,
                    LaborCost = sp.LaborCost
                };
                _db.SubStages.Add(sub);
                await _db.SaveChangesAsync(); // need sub.Id to attach usages

                var muPresets = await _db.MaterialUsagesPreset
                    .Where(mup => mup.SubStagePresetId == sp.Id)
                    .ToListAsync();

                foreach (var mup in muPresets)
                {
                    var mu = new MaterialUsage
                    {
                        SubStageId = sub.Id,
                        MaterialId = mup.MaterialId,
                        Qty = mup.Qty,
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
            await ReloadBuildingsAsync(buildingId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show("Échec de l'ajout de l'étape :\n" + ex.Message, "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}