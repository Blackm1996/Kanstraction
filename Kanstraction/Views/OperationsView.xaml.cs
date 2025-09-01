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

        var subStages = await _db.SubStages
            .Where(ss => ss.StageId == _currentStageId)
            .OrderBy(ss => ss.OrderIndex)
            .Select(ss => new { ss.Id, ss.Name, ss.Status, ss.LaborCost })
            .ToListAsync();

        SubStagesGrid.ItemsSource = subStages;
        MaterialsGrid.ItemsSource = null;
    }

    private async void SubStagesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_db == null) return;
        dynamic? ss = SubStagesGrid.SelectedItem;
        if (ss == null) return;

        int subStageId = (int)ss.Id;

        var rows = await _db.MaterialUsages
            .Where(mu => mu.SubStageId == subStageId)
            .Join(_db.Materials, mu => mu.MaterialId, m => m.Id, (mu, m) => new
            {
                mu.Id,
                MaterialName = m.Name,
                mu.Qty,
                m.Unit,
                mu.UsageDate,
                UnitPrice = m.PricePerUnit,
                Total = m.PricePerUnit * mu.Qty
            })
            .OrderBy(r => r.UsageDate)
            .ToListAsync();

        MaterialsGrid.ItemsSource = rows;
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
            var buildings = await _db.Buildings
                .Where(b => b.ProjectId == _currentProject.Id)
                .OrderBy(b => b.Code)
                .ToListAsync();

            BuildingsGrid.ItemsSource = buildings;

            var newly = buildings.FirstOrDefault(b => b.Code == code);
            if (newly != null)
            {
                BuildingsGrid.SelectedItem = newly;
                BuildingsGrid.ScrollIntoView(newly);
            }
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
        var row = SubStagesGrid.SelectedItem as dynamic;
        if (row == null) return;
        int subStageId = row.Id;

        // Get substage + its stage + its building
        var ss = await _db.SubStages
            .Include(x => x.Stage)
                .ThenInclude(s => s.Building)
            .FirstAsync(x => x.Id == subStageId);

        // Enforce: only one Ongoing substage in the entire building
        bool hasOngoingElsewhere = await _db.SubStages
            .AnyAsync(x => x.Stage.BuildingId == ss.Stage.BuildingId && x.Status == WorkStatus.Ongoing && x.Id != ss.Id);
        if (hasOngoingElsewhere)
        {
            MessageBox.Show("Another sub-stage is already ongoing in this building. Finish it before starting a new one.",
                "Rule", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (ss.Status == WorkStatus.Finished || ss.Status == WorkStatus.Paid || ss.Status == WorkStatus.Stopped)
        {
            MessageBox.Show("This sub-stage cannot be started in its current state.", "Rule", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ss.Status = WorkStatus.Ongoing;
        if (ss.Stage.Status == WorkStatus.NotStarted)
            ss.Stage.Status = WorkStatus.Ongoing;
        if (ss.Stage.Building.Status == WorkStatus.NotStarted)
            ss.Stage.Building.Status = WorkStatus.Ongoing;

        await _db.SaveChangesAsync();

        // Refresh UI
        await ReloadBuildingsAsync(ss.Stage.BuildingId);
        await ReloadStagesAndSubStagesAsync(ss.Stage.BuildingId, ss.StageId);
    }

    private async void FinishSubStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;
        var row = SubStagesGrid.SelectedItem as dynamic;
        if (row == null) return;
        int subStageId = row.Id;

        var ss = await _db.SubStages
            .Include(x => x.Stage)
                .ThenInclude(s => s.SubStages)
            .Include(x => x.Stage.Building)
                .ThenInclude(b => b.Stages)
            .FirstAsync(x => x.Id == subStageId);

        if (ss.Status != WorkStatus.Ongoing && ss.Status != WorkStatus.NotStarted)
        {
            MessageBox.Show("Only NotStarted/Ongoing sub-stages can be finished.", "Rule", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ss.Status = WorkStatus.Finished;

        // If all sub-stages finished → stage finished
        if (ss.Stage.SubStages.All(sx => sx.Status == WorkStatus.Finished || sx.Status == WorkStatus.Paid))
            ss.Stage.Status = WorkStatus.Finished;
        else if (ss.Stage.SubStages.Any(sx => sx.Status == WorkStatus.Ongoing))
            ss.Stage.Status = WorkStatus.Ongoing;
        else
            ss.Stage.Status = WorkStatus.NotStarted; // edge case if others are not started

        // If all stages finished → building finished
        if (ss.Stage.Building.Stages.All(st => st.Status == WorkStatus.Finished || st.Status == WorkStatus.Paid))
            ss.Stage.Building.Status = WorkStatus.Finished;
        else if (ss.Stage.Building.Stages.Any(st => st.Status == WorkStatus.Ongoing))
            ss.Stage.Building.Status = WorkStatus.Ongoing;
        // else leave as-is (could be mixed finished/not started)

        await _db.SaveChangesAsync();

        await ReloadBuildingsAsync(ss.Stage.BuildingId);
        await ReloadStagesAndSubStagesAsync(ss.Stage.BuildingId, ss.StageId);
    }

    private async void ResetSubStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;
        var row = SubStagesGrid.SelectedItem as dynamic;
        if (row == null) return;
        int subStageId = row.Id;

        var ss = await _db.SubStages
            .Include(x => x.Stage)
                .ThenInclude(s => s.SubStages)
            .Include(x => x.Stage.Building)
                .ThenInclude(b => b.Stages)
            .FirstAsync(x => x.Id == subStageId);

        if (ss.Status != WorkStatus.Ongoing)
        {
            MessageBox.Show("Only an ongoing sub-stage can be reset to Not Started.", "Rule", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ss.Status = WorkStatus.NotStarted;

        // Stage status recompute
        if (ss.Stage.SubStages.All(sx => sx.Status == WorkStatus.NotStarted))
            ss.Stage.Status = WorkStatus.NotStarted;
        else if (ss.Stage.SubStages.Any(sx => sx.Status == WorkStatus.Ongoing))
            ss.Stage.Status = WorkStatus.Ongoing;
        else if (ss.Stage.SubStages.All(sx => sx.Status == WorkStatus.Finished || sx.Status == WorkStatus.Paid))
            ss.Stage.Status = WorkStatus.Finished;

        // Building status recompute
        var b = ss.Stage.Building;
        if (b.Stages.All(st => st.Status == WorkStatus.NotStarted))
            b.Status = WorkStatus.NotStarted;
        else if (b.Stages.Any(st => st.Status == WorkStatus.Ongoing))
            b.Status = WorkStatus.Ongoing;
        else if (b.Stages.All(st => st.Status == WorkStatus.Finished || st.Status == WorkStatus.Paid))
            b.Status = WorkStatus.Finished;

        await _db.SaveChangesAsync();

        await ReloadBuildingsAsync(b.Id);
        await ReloadStagesAndSubStagesAsync(b.Id, ss.StageId);
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
}