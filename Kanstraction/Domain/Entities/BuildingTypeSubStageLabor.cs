namespace Kanstraction.Domain.Entities;

public class BuildingTypeSubStageLabor
{
    public int BuildingTypeId { get; set; }
    public int SubStagePresetId { get; set; }
    public decimal? LaborCost { get; set; }
    public bool IncludeInReport { get; set; }

    public BuildingType BuildingType { get; set; } = null!;
    public SubStagePreset SubStagePreset { get; set; } = null!;
}
