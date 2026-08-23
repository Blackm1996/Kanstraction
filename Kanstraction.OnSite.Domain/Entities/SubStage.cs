namespace Kanstraction.OnSite.Domain.Entities;

public class SubStage
{
    private readonly List<MaterialUsage> _materialUsages = new();

    public int Id { get; internal set; }
    public int StageId { get; internal set; }
    public string Name { get; internal set; } = "";
    public int OrderIndex { get; internal set; }
    public WorkStatus Status { get; internal set; } = WorkStatus.NotStarted;
    public DateTime? StartDate { get; internal set; }
    public DateTime? EndDate { get; internal set; }
    public decimal LaborCost { get; internal set; }

    public Stage Stage { get; internal set; } = null!;
    public IReadOnlyCollection<MaterialUsage> MaterialUsages => _materialUsages;
    internal ICollection<MaterialUsage> MutableMaterialUsages => _materialUsages;

    internal SubStage()
    {
    }

    internal static SubStage Create(Stage stage, string name, int orderIndex, decimal laborCost)
    {
        if (laborCost < 0)
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.LaborCostNegative,
                "Labor cost cannot be negative.");

        return new SubStage
        {
            Stage = stage,
            Name = name.Trim(),
            OrderIndex = orderIndex,
            LaborCost = laborCost,
            Status = WorkStatus.NotStarted
        };
    }

    public MaterialUsage AddMaterialUsage(int materialId, decimal quantity, DateTime usageDate)
    {
        if (quantity < 0)
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.MaterialQuantityNegative,
                "Material quantity cannot be negative.");

        var usage = MaterialUsage.Create(this, materialId, quantity, usageDate);
        _materialUsages.Add(usage);
        return usage;
    }

    internal void ShiftOrder(int offset)
        => OrderIndex += offset;

    public void UpdateLaborCost(decimal laborCost)
    {
        if (laborCost < 0)
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.LaborCostNegative,
                "Labor cost cannot be negative.");

        LaborCost = laborCost;
    }

    public void ApplyStatusTransition(WorkStatus newStatus, DateTime today)
    {
        if ((newStatus == WorkStatus.Finished || newStatus == WorkStatus.Paid) && LaborCost <= 0)
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.LaborCostRequired,
                "Set labor cost before marking as Finished/Paid.");

        if (newStatus == WorkStatus.Paid && Status != WorkStatus.Finished)
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.InvalidSubStageTransition,
                "Sub-stage must be Finished before Paid.");

        Status = newStatus;

        switch (newStatus)
        {
            case WorkStatus.NotStarted:
                StartDate = null;
                EndDate = null;
                break;
            case WorkStatus.Ongoing:
                if (StartDate == null)
                    StartDate = today;
                EndDate = null;
                break;
            case WorkStatus.Finished:
            case WorkStatus.Paid:
                if (StartDate == null)
                    StartDate = today;
                EndDate = today;
                var freezeDate = EndDate.Value.Date;
                foreach (var usage in MaterialUsages)
                    usage.MoveUnfrozenUsageTo(freezeDate);
                break;
            case WorkStatus.Stopped:
                if (StartDate == null)
                    StartDate = today;
                EndDate = today;
                break;
        }
    }
}
