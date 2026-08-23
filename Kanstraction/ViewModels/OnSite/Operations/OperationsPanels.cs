using Kanstraction.OnSite.Application.Common;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace Kanstraction.ViewModels.OnSite.Operations;

public sealed class BuildingsPanelViewModel : ObservableObject
{
    private readonly IReadOnlyList<WorkStatusDto> _statuses =
        Enum.GetValues<WorkStatusDto>();
    private readonly HashSet<WorkStatusDto> _selectedStatuses;
    private string _searchText = string.Empty;
    private string _statusFilterLabel = string.Empty;
    private BuildingRowViewModel? _selectedBuilding;
    private ICollectionView? _itemsView;

    public BuildingsPanelViewModel()
    {
        _selectedStatuses = _statuses.ToHashSet();
        UpdateFilterLabel();
    }

    public ObservableCollection<BuildingRowViewModel> Items { get; } = [];
    public ICollectionView? ItemsView
    {
        get => _itemsView;
        private set => SetProperty(ref _itemsView, value);
    }

    public BuildingRowViewModel? SelectedBuilding
    {
        get => _selectedBuilding;
        set => SetProperty(ref _selectedBuilding, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ItemsView?.Refresh();
            }
        }
    }

    public string StatusFilterLabel
    {
        get => _statusFilterLabel;
        private set => SetProperty(ref _statusFilterLabel, value);
    }

    public bool ShowAllStatuses
    {
        get => _selectedStatuses.Count == _statuses.Count;
        set
        {
            _selectedStatuses.Clear();
            if (value)
            {
                _selectedStatuses.UnionWith(_statuses);
            }
            RefreshFilter();
        }
    }

    public bool ShowNotStarted
    {
        get => Includes(WorkStatusDto.NotStarted);
        set => SetStatus(WorkStatusDto.NotStarted, value);
    }

    public bool ShowOngoing
    {
        get => Includes(WorkStatusDto.Ongoing);
        set => SetStatus(WorkStatusDto.Ongoing, value);
    }

    public bool ShowFinished
    {
        get => Includes(WorkStatusDto.Finished);
        set => SetStatus(WorkStatusDto.Finished, value);
    }

    public bool ShowPaid
    {
        get => Includes(WorkStatusDto.Paid);
        set => SetStatus(WorkStatusDto.Paid, value);
    }

    public bool ShowStopped
    {
        get => Includes(WorkStatusDto.Stopped);
        set => SetStatus(WorkStatusDto.Stopped, value);
    }

    public void ReplaceItems(
        IEnumerable<BuildingRowViewModel> buildings,
        int? preferredBuildingId = null)
    {
        Items.Clear();
        foreach (var building in buildings)
        {
            Items.Add(building);
        }

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = Filter;
        ItemsView.Refresh();

        var visible = ItemsView.Cast<BuildingRowViewModel>().ToList();
        SelectedBuilding =
            visible.FirstOrDefault(item => item.Id == preferredBuildingId) ??
            visible.FirstOrDefault();
    }

    public void Clear()
    {
        Items.Clear();
        SelectedBuilding = null;
        ItemsView = null;
    }

    private bool Includes(WorkStatusDto status) => _selectedStatuses.Contains(status);

    private void SetStatus(WorkStatusDto status, bool selected)
    {
        if (_selectedStatuses.Count == _statuses.Count)
        {
            _selectedStatuses.Clear();
            _selectedStatuses.Add(status);
        }
        else if (selected)
        {
            _selectedStatuses.Add(status);
        }
        else
        {
            _selectedStatuses.Remove(status);
        }
        RefreshFilter();
    }

    private bool Filter(object value)
    {
        if (value is not BuildingRowViewModel row ||
            !_selectedStatuses.Contains(row.Status))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(SearchText) ||
               row.Code.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshFilter()
    {
        OnPropertyChanged(nameof(ShowAllStatuses));
        OnPropertyChanged(nameof(ShowNotStarted));
        OnPropertyChanged(nameof(ShowOngoing));
        OnPropertyChanged(nameof(ShowFinished));
        OnPropertyChanged(nameof(ShowPaid));
        OnPropertyChanged(nameof(ShowStopped));
        UpdateFilterLabel();
        ItemsView?.Refresh();

        var visible = ItemsView?.Cast<BuildingRowViewModel>().ToList() ?? [];
        if (SelectedBuilding == null || !visible.Contains(SelectedBuilding))
        {
            SelectedBuilding = visible.FirstOrDefault();
        }
    }

    private void UpdateFilterLabel()
    {
        if (_selectedStatuses.Count == 0)
        {
            StatusFilterLabel = ResourceHelper.GetString(
                "OperationsView_StatusFilter_None",
                "No statuses selected");
            return;
        }

        if (_selectedStatuses.Count == _statuses.Count)
        {
            StatusFilterLabel = ResourceHelper.GetString(
                "OperationsView_StatusFilter_All",
                "All statuses");
            return;
        }

        StatusFilterLabel = string.Join(
            ", ",
            _selectedStatuses
                .OrderBy(status => status)
                .Select(StatusDisplayName));
    }

    private static string StatusDisplayName(WorkStatusDto status)
    {
        var key = status switch
        {
            WorkStatusDto.NotStarted => "WorkStatus_NotStarted",
            WorkStatusDto.Ongoing => "WorkStatus_Ongoing",
            WorkStatusDto.Finished => "WorkStatus_Finished",
            WorkStatusDto.Paid => "WorkStatus_Paid",
            WorkStatusDto.Stopped => "WorkStatus_Stopped",
            _ => null
        };
        return key == null ? status.ToString() : ResourceHelper.GetString(key, status.ToString());
    }
}

public sealed class StagesPanelViewModel : ObservableObject
{
    private StageRowViewModel? _selectedStage;

    public ObservableCollection<StageRowViewModel> Items { get; } = [];

    public StageRowViewModel? SelectedStage
    {
        get => _selectedStage;
        set => SetProperty(ref _selectedStage, value);
    }

    public void ReplaceItems(IEnumerable<StageRowViewModel> stages, int? preferredStageId)
    {
        Items.Clear();
        foreach (var stage in stages)
        {
            Items.Add(stage);
        }
        SelectedStage =
            Items.FirstOrDefault(item => item.Id == preferredStageId) ??
            Items.FirstOrDefault();
    }

    public void Clear()
    {
        Items.Clear();
        SelectedStage = null;
    }
}

public sealed class SubStagesPanelViewModel : ObservableObject
{
    private SubStageRowViewModel? _selectedSubStage;
    private decimal _totalLaborCost;

    public ObservableCollection<SubStageRowViewModel> Items { get; } = [];

    public SubStageRowViewModel? SelectedSubStage
    {
        get => _selectedSubStage;
        set => SetProperty(ref _selectedSubStage, value);
    }

    public decimal TotalLaborCost
    {
        get => _totalLaborCost;
        private set => SetProperty(ref _totalLaborCost, value);
    }

    public void ReplaceItems(IEnumerable<SubStageRowViewModel> subStages, int? preferredSubStageId = null)
    {
        Items.Clear();
        foreach (var subStage in subStages)
        {
            Items.Add(subStage);
            subStage.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SubStageRowViewModel.LaborCost))
                {
                    RecalculateTotal();
                }
            };
        }
        RecalculateTotal();
        SelectedSubStage =
            Items.FirstOrDefault(item => item.Id == preferredSubStageId) ??
            Items.FirstOrDefault();
    }

    public void RecalculateTotal() => TotalLaborCost = Items.Sum(item => item.LaborCost);

    public void Clear()
    {
        Items.Clear();
        SelectedSubStage = null;
        TotalLaborCost = 0;
    }
}

public sealed class MaterialUsagesPanelViewModel : ObservableObject
{
    private decimal _totalCost;

    public ObservableCollection<MaterialUsageRowViewModel> Items { get; } = [];

    public decimal TotalCost
    {
        get => _totalCost;
        private set => SetProperty(ref _totalCost, value);
    }

    public void ReplaceItems(IEnumerable<MaterialUsageRowViewModel> usages)
    {
        Items.Clear();
        foreach (var usage in usages)
        {
            Items.Add(usage);
            usage.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MaterialUsageRowViewModel.Qty))
                {
                    RecalculateTotal();
                }
            };
        }
        RecalculateTotal();
    }

    public void RecalculateTotal() =>
        TotalCost = Items.Sum(item => item.Qty * item.DisplayUnitPrice);

    public void Clear()
    {
        Items.Clear();
        TotalCost = 0;
    }
}
