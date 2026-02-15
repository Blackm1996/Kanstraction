namespace Kanstraction.Domain.Entities;

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

    public void ChangeStageStatus(int stageId, WorkStatus newStatus, DateTime today)
    {
        var stage = Stages.FirstOrDefault(x => x.Id == stageId);
        if (stage == null)
            throw new InvalidOperationException($"Stage {stageId} does not belong to building {Id}.");

        if (newStatus == WorkStatus.Stopped)
        {
            foreach (var buildingStage in Stages)
                buildingStage.ApplyStatusTransition(WorkStatus.Stopped, today);

            Status = WorkStatus.Stopped;
            return;
        }

        stage.ApplyStatusTransition(newStatus, today);
        RecomputeStatusFromStages();
    }

    public void RecomputeStatusFromStages()
    {
        if (Stages.Count == 0)
        {
            Status = WorkStatus.NotStarted;
            return;
        }

        if (Stages.All(x => x.Status == WorkStatus.NotStarted))
            Status = WorkStatus.NotStarted;
        else if (Stages.All(x => x.Status == WorkStatus.Paid))
            Status = WorkStatus.Paid;
        else if (Stages.All(x => x.Status == WorkStatus.Finished || x.Status == WorkStatus.Paid))
            Status = WorkStatus.Finished;
        else if (Stages.Any(x => x.Status == WorkStatus.Stopped) && !Stages.Any(x => x.Status == WorkStatus.Ongoing))
            Status = WorkStatus.Stopped;
        else
            Status = WorkStatus.Ongoing;
    }
}
