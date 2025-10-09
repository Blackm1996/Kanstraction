namespace Kanstraction.Entities;

public class BuildingTypeMaterialUsage
{
    public int BuildingTypeId { get; set; }
    public int MaterialUsagePresetId { get; set; }
    public decimal Qty { get; set; }

    public BuildingType BuildingType { get; set; } = null!;
    public MaterialUsagePreset MaterialUsagePreset { get; set; } = null!;
}
