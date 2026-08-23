namespace Kanstraction.OnSite.Domain.Entities;

public class MaterialUsage
{
    public int Id { get; internal set; }
    public int SubStageId { get; internal set; }
    public int MaterialId { get; internal set; }
    public decimal Qty { get; internal set; }
    public DateTime UsageDate { get; internal set; }
    public string? Notes { get; internal set; }

    public SubStage SubStage { get; internal set; } = null!;
    public Material Material { get; internal set; } = null!;

    internal MaterialUsage()
    {
    }

    internal static MaterialUsage Create(
        SubStage subStage,
        int materialId,
        decimal quantity,
        DateTime usageDate)
        => new()
        {
            SubStage = subStage,
            MaterialId = materialId,
            Qty = quantity,
            UsageDate = usageDate.Date
        };

    public void MoveUnfrozenUsageTo(DateTime usageDate)
        => UsageDate = usageDate.Date;

    public void UpdateQuantity(decimal quantity)
    {
        if (quantity < 0)
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.MaterialQuantityNegative,
                "Material quantity cannot be negative.");

        Qty = quantity;
    }
}
