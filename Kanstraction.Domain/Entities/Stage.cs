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

    public void ApplyStatusTransition(WorkStatus newStatus, DateTime today)
    {
        if (newStatus == WorkStatus.Finished || newStatus == WorkStatus.Paid)
        {
            if (SubStages.Count == 0)
                throw new InvalidOperationException("Stage has no sub-stages; cannot mark as Finished/Paid.");
        }

        foreach (var subStage in SubStages)
            subStage.ApplyStatusTransition(newStatus, today);

        Status = newStatus;

        switch (newStatus)
        {
            case WorkStatus.NotStarted:
                StartDate = null;
                EndDate = null;
                break;
            case WorkStatus.Ongoing:
                if (StartDate == null)
                    StartDate = today;
                EndDate = null;
                break;
            case WorkStatus.Finished:
            case WorkStatus.Paid:
            case WorkStatus.Stopped:
                if (StartDate == null)
                    StartDate = today;
                EndDate = today;
                break;
        }
    }
}
