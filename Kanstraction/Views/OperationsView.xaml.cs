using Kanstraction.Data;
using Kanstraction.Entities;
using Kanstraction.Services;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

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

        var stageRows = await _db.Stages
            .Where(s => s.BuildingId == _currentBuildingId)
            .OrderBy(s => s.OrderIndex)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Status,
                OngoingSubStageName = _db.SubStages.Where(ss => ss.StageId == s.Id && ss.Status == WorkStatus.Ongoing)
                                                   .OrderBy(ss => ss.OrderIndex)
                                                   .Select(ss => ss.Name).FirstOrDefault() ?? ""
            })
            .ToListAsync();

        var stageProgress = await ProgressService.ComputeStagesProgressAsync(_db, stageRows.Select(r => r.Id));

        var stages = stageRows.Select(r => new
        {
            r.Id,
            r.Name,
            r.Status,
            r.OngoingSubStageName,
            ProgressPercent = (int)Math.Round((stageProgress.TryGetValue(r.Id, out var v) ? v : 0.0) * 100.0)
        }).ToList();

        StagesGrid.ItemsSource = stages;
        SubStagesGrid.ItemsSource = null;
        MaterialsGrid.ItemsSource = null;

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
        MaterialsGrid.ItemsSource = null;
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
                MessageBox.Show("Labor cost cannot be negative.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show("Quantity cannot be negative.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("Select a project first.", "No project", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("A building with this code already exists in this project.", "Duplicate code",
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
            MessageBox.Show("Failed to create building:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        var confirm = MessageBox.Show("Stop this building? All not finished/paid stages & sub-stages will be marked Stopped.",
            "Confirm stop", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
            MessageBox.Show("Failed to stop building:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("Another sub-stage is already ongoing in this building. Finish it first.",
                "Rule", MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show("You must finish the previous sub-stage first.", "Order rule",
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
                MessageBox.Show("You must finish all earlier stages before starting this stage.", "Order rule",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // State checks
        if (ss.Status == WorkStatus.Finished || ss.Status == WorkStatus.Paid || ss.Status == WorkStatus.Stopped)
        {
            MessageBox.Show("This sub-stage cannot be started in its current state.", "Rule",
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
            MessageBox.Show("You can only finish a sub-stage that is Ongoing.", "Rule",
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
            MessageBox.Show("Only an Ongoing sub-stage can be reset to Not Started.", "Rule",
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

        if (stageId != 0)
        {
            var subs = await _db.SubStages
                .Where(ss => ss.StageId == stageId)
                .OrderBy(ss => ss.OrderIndex)
                .AsNoTracking()
                .ToListAsync();
            SubStagesGrid.ItemsSource = subs;
            // materials list can be reloaded similarly if needed
        }
        else
        {
            SubStagesGrid.ItemsSource = null;
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
            MessageBox.Show("Select a sub-stage first.", "No sub-stage", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show("Please select a material.", "Required", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("Failed to add material:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("Failed to add sub-stage:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}