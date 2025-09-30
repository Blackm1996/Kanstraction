namespace Kanstraction.Entities;

public class SubStagePreset
{
    public int Id { get; set; }
    public int StagePresetId { get; set; }
    public string Name { get; set; } = "";
    public int OrderIndex { get; set; }
    public decimal? LaborCost { get; set; }
    public StagePreset StagePreset { get; set; } = null!;
}
