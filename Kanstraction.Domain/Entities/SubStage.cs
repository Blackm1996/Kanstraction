namespace Kanstraction.Domain.Entities;

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

    public void ApplyStatusTransition(WorkStatus newStatus, DateTime today)
    {
        if ((newStatus == WorkStatus.Finished || newStatus == WorkStatus.Paid) && LaborCost <= 0)
            throw new InvalidOperationException("Set labor cost before marking as Finished/Paid.");

        if (newStatus == WorkStatus.Paid && Status != WorkStatus.Finished)
            throw new InvalidOperationException("Sub-stage must be Finished before Paid.");

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
                if (StartDate == null)
                    StartDate = today;
                EndDate = today;
                var freezeDate = EndDate.Value.Date;
                foreach (var usage in MaterialUsages)
                    usage.UsageDate = freezeDate;
                break;
            case WorkStatus.Stopped:
                if (StartDate == null)
                    StartDate = today;
                EndDate = today;
                break;
        }
    }
}
