namespace Kanstraction.Domain.Entities;

public class Stage
{
    public int Id { get; set; }
    public int BuildingId { get; set; }
    public string Name { get; set; } = "";
    public int OrderIndex { get; set; }
    public WorkStatus Status { get; set; } = WorkStatus.NotStarted;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Notes { get; set; }

    public Building Building { get; set; } = null!;
    public ICollection<SubStage> SubStages { get; set; } = new List<SubStage>();
}