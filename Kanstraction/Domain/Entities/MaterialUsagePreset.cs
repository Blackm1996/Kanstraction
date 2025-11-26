namespace Kanstraction.Domain.Entities;

public class MaterialUsagePreset
{
    public int Id { get; set; }
    public int SubStagePresetId { get; set; }
    public int MaterialId { get; set; }
    public SubStagePreset SubStagePreset { get; set; } = null!;
    public Material Material { get; set; } = null!;
}
