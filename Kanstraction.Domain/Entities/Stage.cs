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

    public void RecomputeStatusFromSubStages(DateTime today)
    {
        if (SubStages.Count == 0)
        {
            Status = WorkStatus.NotStarted;
            StartDate = null;
            EndDate = null;
            return;
        }

        var allPaid = SubStages.All(ss => ss.Status == WorkStatus.Paid);
        var allStopped = SubStages.All(ss => ss.Status == WorkStatus.Stopped);
        var anyStopped = SubStages.Any(ss => ss.Status == WorkStatus.Stopped);
        var anyOngoing = SubStages.Any(ss => ss.Status == WorkStatus.Ongoing);
        var allNotStarted = SubStages.All(ss => ss.Status == WorkStatus.NotStarted);
        var allFinishedOrPaid = SubStages.All(ss => ss.Status == WorkStatus.Finished || ss.Status == WorkStatus.Paid);
        var anyFinishedOrPaid = SubStages.Any(ss => ss.Status == WorkStatus.Finished || ss.Status == WorkStatus.Paid);

        if (allStopped)
            Status = WorkStatus.Stopped;
        else if (allPaid)
            Status = WorkStatus.Paid;
        else if (allFinishedOrPaid)
            Status = WorkStatus.Finished;
        else if (anyOngoing)
            Status = WorkStatus.Ongoing;
        else if (anyStopped)
            Status = WorkStatus.Stopped;
        else if (allNotStarted)
            Status = WorkStatus.NotStarted;
        else if (anyFinishedOrPaid)
            Status = WorkStatus.Ongoing;
        else
            Status = WorkStatus.Ongoing;

        if (Status == WorkStatus.Ongoing && StartDate == null)
            StartDate = today;

        if ((Status == WorkStatus.Finished || Status == WorkStatus.Paid || Status == WorkStatus.Stopped) && EndDate == null)
            EndDate = today;

        if (Status == WorkStatus.NotStarted)
        {
            StartDate = null;
            EndDate = null;
        }
        else if (Status == WorkStatus.Stopped && StartDate == null)
        {
            StartDate = today;
        }
    }

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
