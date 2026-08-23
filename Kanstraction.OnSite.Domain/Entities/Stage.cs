namespace Kanstraction.OnSite.Domain.Entities;

public class Stage
{
    private readonly List<SubStage> _subStages = new();

    public int Id { get; internal set; }
    public int BuildingId { get; internal set; }
    public string Name { get; internal set; } = "";
    public int OrderIndex { get; internal set; }
    public WorkStatus Status { get; internal set; } = WorkStatus.NotStarted;
    public DateTime? StartDate { get; internal set; }
    public DateTime? EndDate { get; internal set; }
    public string? Notes { get; internal set; }

    public Building Building { get; internal set; } = null!;
    public IReadOnlyCollection<SubStage> SubStages => _subStages;
    internal ICollection<SubStage> MutableSubStages => _subStages;

    internal Stage()
    {
    }

    internal static Stage Create(Building building, string name, int orderIndex)
        => new()
        {
            Building = building,
            Name = name.Trim(),
            OrderIndex = orderIndex,
            Status = WorkStatus.NotStarted
        };

    public SubStage AddSubStage(string name, int orderIndex, decimal laborCost)
    {
        var subStage = SubStage.Create(this, name, orderIndex, laborCost);
        _subStages.Add(subStage);
        return subStage;
    }

    internal void ShiftOrder(int offset)
        => OrderIndex += offset;

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
                throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                    Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.StageHasNoSubStages,
                    "Stage has no sub-stages; cannot mark as Finished/Paid.");
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
