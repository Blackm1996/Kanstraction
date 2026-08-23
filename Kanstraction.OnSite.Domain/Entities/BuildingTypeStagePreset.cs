namespace Kanstraction.OnSite.Domain.Entities;

public class BuildingTypeStagePreset
{
    public int BuildingTypeId { get; internal set; }
    public int StagePresetId { get; internal set; }
    public int OrderIndex { get; internal set; }
    public BuildingType BuildingType { get; internal set; } = null!;
    public StagePreset StagePreset { get; internal set; } = null!;

    internal BuildingTypeStagePreset()
    {
    }

    public static BuildingTypeStagePreset Create(
        int buildingTypeId,
        int stagePresetId,
        int orderIndex)
    {
        if (orderIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(orderIndex));

        return new BuildingTypeStagePreset
        {
            BuildingTypeId = buildingTypeId,
            StagePresetId = stagePresetId,
            OrderIndex = orderIndex
        };
    }

    public void UpdateOrder(int orderIndex)
    {
        if (orderIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(orderIndex));

        OrderIndex = orderIndex;
    }
}
