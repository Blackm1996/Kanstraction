namespace Kanstraction.OnSite.Domain.Entities;

public class MaterialUsagePreset
{
    public int Id { get; internal set; }
    public int SubStagePresetId { get; internal set; }
    public int MaterialId { get; internal set; }
    public SubStagePreset SubStagePreset { get; internal set; } = null!;
    public Material Material { get; internal set; } = null!;

    internal MaterialUsagePreset()
    {
    }

    public static MaterialUsagePreset Create(int subStagePresetId, int materialId)
    {
        if (subStagePresetId <= 0)
            throw new ArgumentOutOfRangeException(nameof(subStagePresetId));
        if (materialId <= 0)
            throw new ArgumentOutOfRangeException(nameof(materialId));

        return new MaterialUsagePreset
        {
            SubStagePresetId = subStagePresetId,
            MaterialId = materialId
        };
    }
}
