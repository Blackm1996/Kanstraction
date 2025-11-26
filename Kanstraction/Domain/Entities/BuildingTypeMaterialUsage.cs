namespace Kanstraction.Domain.Entities;

public class BuildingTypeMaterialUsage
{
    public int BuildingTypeId { get; set; }
    public int SubStagePresetId { get; set; }
    public int MaterialId { get; set; }
    public decimal? Qty { get; set; }

    public BuildingType BuildingType { get; set; } = null!;
    public SubStagePreset SubStagePreset { get; set; } = null!;
    public Material Material { get; set; } = null!;
}
