namespace Kanstraction.Domain.Entities;
public class StagePreset
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public ICollection<SubStagePreset> SubStages { get; set; } = new List<SubStagePreset>();
}