using Kanstraction.Application.Operations;
using Kanstraction.Domain.Entities;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Kanstraction.Presentation.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IProjectCatalogService _projectCatalogService;

    private ProjectSummaryViewModel? _selectedProject;
    private BuildingViewModel? _selectedBuilding;
    private StageViewModel? _selectedStage;
    private SubStageViewModel? _selectedSubStage;
    private bool _isBusy;

    public MainViewModel(IProjectCatalogService projectCatalogService)
    {
        _projectCatalogService = projectCatalogService;

        Projects = new ObservableCollection<ProjectSummaryViewModel>();
        Buildings = new ObservableCollection<BuildingViewModel>();
        Stages = new ObservableCollection<StageViewModel>();
        SubStages = new ObservableCollection<SubStageViewModel>();
        Materials = new ObservableCollection<MaterialUsageViewModel>();

        RefreshCommand = new AsyncRelayCommand(_ => InitializeAsync(), _ => !IsBusy);
    }

    public ObservableCollection<ProjectSummaryViewModel> Projects { get; }

    public ObservableCollection<BuildingViewModel> Buildings { get; }

    public ObservableCollection<StageViewModel> Stages { get; }

    public ObservableCollection<SubStageViewModel> SubStages { get; }

    public ObservableCollection<MaterialUsageViewModel> Materials { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ProjectSummaryViewModel? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                _ = LoadProjectDetailsAsync(value);
            }
        }
    }

    public BuildingViewModel? SelectedBuilding
    {
        get => _selectedBuilding;
        set
        {
            if (SetProperty(ref _selectedBuilding, value))
            {
                UpdateStages(value);
            }
        }
    }

    public StageViewModel? SelectedStage
    {
        get => _selectedStage;
        set
        {
            if (SetProperty(ref _selectedStage, value))
            {
                UpdateSubStages(value);
            }
        }
    }

    public SubStageViewModel? SelectedSubStage
    {
        get => _selectedSubStage;
        set
        {
            if (SetProperty(ref _selectedSubStage, value))
            {
                UpdateMaterials(value);
            }
        }
    }

    public async Task InitializeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await LoadProjectsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadProjectsAsync()
    {
        Projects.Clear();
        Buildings.Clear();
        Stages.Clear();
        SubStages.Clear();
        Materials.Clear();

        var projects = await _projectCatalogService.GetProjectsAsync();
        foreach (var dto in projects)
        {
            Projects.Add(new ProjectSummaryViewModel(dto.Id, dto.Name));
        }

        SelectedProject = Projects.FirstOrDefault();
    }

    private async Task LoadProjectDetailsAsync(ProjectSummaryViewModel? project)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            Buildings.Clear();
            Stages.Clear();
            SubStages.Clear();
            Materials.Clear();

            if (project == null)
            {
                return;
            }

            var details = await _projectCatalogService.GetProjectAsync(project.Id);
            if (details == null)
            {
                return;
            }

            foreach (var building in details.Buildings)
            {
                var buildingVm = BuildingViewModel.FromDto(building);
                Buildings.Add(buildingVm);
            }

            SelectedBuilding = Buildings.FirstOrDefault();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateStages(BuildingViewModel? building)
    {
        Stages.Clear();
        SubStages.Clear();
        Materials.Clear();

        if (building == null)
        {
            return;
        }

        foreach (var stage in building.Stages)
        {
            Stages.Add(stage);
        }

        SelectedStage = Stages.FirstOrDefault();
    }

    private void UpdateSubStages(StageViewModel? stage)
    {
        SubStages.Clear();
        Materials.Clear();

        if (stage == null)
        {
            return;
        }

        foreach (var subStage in stage.SubStages)
        {
            SubStages.Add(subStage);
        }

        SelectedSubStage = SubStages.FirstOrDefault();
    }

    private void UpdateMaterials(SubStageViewModel? subStage)
    {
        Materials.Clear();
        if (subStage == null)
        {
            return;
        }

        foreach (var material in subStage.Materials)
        {
            Materials.Add(material);
        }
    }
}

public sealed record ProjectSummaryViewModel(int Id, string Name);

public sealed class BuildingViewModel : ObservableObject
{
    private readonly ObservableCollection<StageViewModel> _stages;

    private BuildingViewModel(int id, string code, string typeName, WorkStatus status, double progress, IEnumerable<StageViewModel> stages)
    {
        Id = id;
        Code = code;
        TypeName = typeName;
        Status = status;
        Progress = progress;
        _stages = new ObservableCollection<StageViewModel>(stages);
    }

    public int Id { get; }
    public string Code { get; }
    public string TypeName { get; }
    public WorkStatus Status { get; }
    public double Progress { get; }
    public string ProgressDisplay => Progress.ToString("P0");

    public IReadOnlyList<StageViewModel> Stages => _stages;

    public static BuildingViewModel FromDto(BuildingDto dto)
    {
        var stages = dto.Stages.Select(StageViewModel.FromDto);
        return new BuildingViewModel(dto.Id, dto.Code, dto.TypeName, dto.Status, dto.Progress, stages);
    }
}

public sealed class StageViewModel : ObservableObject
{
    private readonly ObservableCollection<SubStageViewModel> _subStages;

    private StageViewModel(int id, string name, WorkStatus status, int orderIndex, double progress, IEnumerable<SubStageViewModel> subStages)
    {
        Id = id;
        Name = name;
        Status = status;
        OrderIndex = orderIndex;
        Progress = progress;
        _subStages = new ObservableCollection<SubStageViewModel>(subStages);
    }

    public int Id { get; }
    public string Name { get; }
    public WorkStatus Status { get; }
    public int OrderIndex { get; }
    public double Progress { get; }
    public string ProgressDisplay => Progress.ToString("P0");

    public IReadOnlyList<SubStageViewModel> SubStages => _subStages;

    public static StageViewModel FromDto(StageDto dto)
    {
        var subStages = dto.SubStages.Select(SubStageViewModel.FromDto);
        return new StageViewModel(dto.Id, dto.Name, dto.Status, dto.OrderIndex, dto.Progress, subStages);
    }
}

public sealed class SubStageViewModel : ObservableObject
{
    private readonly ObservableCollection<MaterialUsageViewModel> _materials;

    private SubStageViewModel(int id, string name, WorkStatus status, int orderIndex, decimal laborCost, DateTime? startDate, DateTime? endDate, IEnumerable<MaterialUsageViewModel> materials)
    {
        Id = id;
        Name = name;
        Status = status;
        OrderIndex = orderIndex;
        LaborCost = laborCost;
        StartDate = startDate;
        EndDate = endDate;
        _materials = new ObservableCollection<MaterialUsageViewModel>(materials);
    }

    public int Id { get; }
    public string Name { get; }
    public WorkStatus Status { get; }
    public int OrderIndex { get; }
    public decimal LaborCost { get; }
    public DateTime? StartDate { get; }
    public DateTime? EndDate { get; }

    public IReadOnlyList<MaterialUsageViewModel> Materials => _materials;

    public static SubStageViewModel FromDto(SubStageDto dto)
    {
        var materials = dto.Materials.Select(MaterialUsageViewModel.FromDto);
        return new SubStageViewModel(dto.Id, dto.Name, dto.Status, dto.OrderIndex, dto.LaborCost, dto.StartDate, dto.EndDate, materials);
    }
}

public sealed record MaterialUsageViewModel(
    int Id,
    string MaterialName,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal TotalCost,
    DateTime UsageDate,
    string? Notes)
{
    public static MaterialUsageViewModel FromDto(MaterialUsageDto dto) =>
        new(dto.Id, dto.MaterialName, dto.Unit, dto.Quantity, dto.UnitPrice, dto.TotalCost, dto.UsageDate, dto.Notes);
}
