namespace Kanstraction.Entities;

public class BuildingTypeStagePreset
{
    public int BuildingTypeId { get; set; }
    public int StagePresetId { get; set; }
    public int OrderIndex { get; set; }
    public BuildingType BuildingType { get; set; } = null!;
    public StagePreset StagePreset { get; set; } = null!;
}
