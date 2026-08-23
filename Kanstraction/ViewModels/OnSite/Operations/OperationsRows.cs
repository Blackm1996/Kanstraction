using Kanstraction.OnSite.Application.Common;

namespace Kanstraction.ViewModels.OnSite.Operations;

public sealed class BuildingRowViewModel
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string TypeName { get; init; } = string.Empty;
    public WorkStatusDto Status { get; init; }
    public int ProgressPercent { get; init; }
    public string CurrentStageName { get; init; } = string.Empty;
    public bool HasPaidItems { get; init; }
}

public sealed class StageRowViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public WorkStatusDto Status { get; init; }
    public double ProgressPercent { get; init; }
    public string OngoingSubStageName { get; init; } = string.Empty;
    public DateTime? PaidDate { get; init; }
}

public sealed class SubStageRowViewModel : ObservableObject
{
    private decimal _laborCost;

    public int Id { get; init; }
    public int StageId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int OrderIndex { get; init; }
    public WorkStatusDto Status { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public DateTime? PaidDate => Status == WorkStatusDto.Paid ? EndDate : null;

    public decimal LaborCost
    {
        get => _laborCost;
        set => SetProperty(ref _laborCost, value);
    }
}

public sealed class MaterialUsageRowViewModel : ObservableObject
{
    private decimal _quantity;

    public int Id { get; init; }
    public int SubStageId { get; init; }
    public string MaterialName { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public DateTime UsageDate { get; init; }
    public decimal DisplayUnitPrice { get; init; }

    public decimal Qty
    {
        get => _quantity;
        set => SetProperty(ref _quantity, value);
    }
}
