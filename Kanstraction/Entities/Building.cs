namespace Kanstraction.Entities;

public class Building
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? BuildingTypeId { get; set; }
    public string Code { get; set; } = "";
    public WorkStatus Status { get; set; } = WorkStatus.NotStarted;
    public Project Project { get; set; } = null!;
    public BuildingType? BuildingType { get; set; }
    public ICollection<Stage> Stages { get; set; } = new List<Stage>();
}
