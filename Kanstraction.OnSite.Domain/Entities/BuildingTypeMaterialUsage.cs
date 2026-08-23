namespace Kanstraction.OnSite.Domain.Entities;

public class BuildingTypeMaterialUsage
{
    public int BuildingTypeId { get; internal set; }
    public int SubStagePresetId { get; internal set; }
    public int MaterialId { get; internal set; }
    public decimal? Qty { get; internal set; }
    public BuildingType BuildingType { get; internal set; } = null!;
    public SubStagePreset SubStagePreset { get; internal set; } = null!;
    public Material Material { get; internal set; } = null!;

    internal BuildingTypeMaterialUsage()
    {
    }

    public static BuildingTypeMaterialUsage Create(
        int buildingTypeId,
        int subStagePresetId,
        int materialId,
        decimal? quantity)
    {
        var usage = new BuildingTypeMaterialUsage
        {
            BuildingTypeId = buildingTypeId,
            SubStagePresetId = subStagePresetId,
            MaterialId = materialId
        };
        usage.UpdateQuantity(quantity);
        return usage;
    }

    public void UpdateQuantity(decimal? quantity)
    {
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        Qty = quantity;
    }
}
