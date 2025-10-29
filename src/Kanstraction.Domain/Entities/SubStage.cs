namespace Kanstraction.Entities;

public class SubStage
{
    public int Id { get; set; }
    public int StageId { get; set; }
    public string Name { get; set; } = "";
    public int OrderIndex { get; set; }
    public WorkStatus Status { get; set; } = WorkStatus.NotStarted;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal LaborCost { get; set; }

    public Stage Stage { get; set; } = null!;
    public ICollection<MaterialUsage> MaterialUsages { get; set; } = new List<MaterialUsage>();
}
