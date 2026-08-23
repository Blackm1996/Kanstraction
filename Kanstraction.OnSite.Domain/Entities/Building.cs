namespace Kanstraction.OnSite.Domain.Entities;

public class Building
{
    private readonly List<Stage> _stages = new();

    public int Id { get; internal set; }
    public int ProjectId { get; internal set; }
    public int? BuildingTypeId { get; internal set; }
    public string Code { get; internal set; } = "";
    public WorkStatus Status { get; internal set; } = WorkStatus.NotStarted;
    public Project Project { get; internal set; } = null!;
    public BuildingType? BuildingType { get; internal set; }
    public IReadOnlyCollection<Stage> Stages => _stages;
    internal ICollection<Stage> MutableStages => _stages;

    internal Building()
    {
    }

    public static Building Create(int projectId, int buildingTypeId, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.BuildingCodeRequired,
                "A building code is required.");

        return new Building
        {
            ProjectId = projectId,
            BuildingTypeId = buildingTypeId,
            Code = code.Trim(),
            Status = WorkStatus.NotStarted
        };
    }

    public Stage AddStage(string name, int orderIndex)
    {
        var stage = Stage.Create(this, name, orderIndex);
        _stages.Add(stage);
        return stage;
    }

    public Stage InsertStage(string name, int desiredOrder)
    {
        var order = Math.Clamp(desiredOrder, 1, Stages.Count + 1);
        foreach (var sibling in Stages.Where(item => item.OrderIndex >= order))
            sibling.ShiftOrder(1);
        return AddStage(name, order);
    }

    public void ChangeStageStatus(int stageId, WorkStatus newStatus, DateTime today)
    {
        var stage = Stages.FirstOrDefault(x => x.Id == stageId);
        if (stage == null)
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.StageDoesNotBelongToBuilding,
                $"Stage {stageId} does not belong to building {Id}.");

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

    public void StartSubStage(int subStageId, DateTime today)
    {
        var (stage, subStage) = FindSubStage(subStageId);

        if (Stages.SelectMany(x => x.SubStages)
            .Any(x => x.Id != subStageId && x.Status == WorkStatus.Ongoing))
        {
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.AnotherSubStageIsOngoing,
                "Another sub-stage is already in progress in this building.");
        }

        if (subStage.OrderIndex > 1)
        {
            var previous = stage.SubStages.FirstOrDefault(x => x.OrderIndex == subStage.OrderIndex - 1);
            if (previous == null || previous.Status is not (WorkStatus.Finished or WorkStatus.Paid))
            {
                throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                    Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.PreviousSubStageIncomplete,
                    "The previous sub-stage must be finished first.");
            }
        }

        if (stage.OrderIndex > 1 &&
            Stages.Where(x => x.OrderIndex < stage.OrderIndex)
                .Any(x => x.Status is not (WorkStatus.Finished or WorkStatus.Paid)))
        {
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.PreviousStageIncomplete,
                "All previous stages must be finished first.");
        }

        if (subStage.Status is WorkStatus.Finished or WorkStatus.Paid or WorkStatus.Stopped)
        {
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.InvalidSubStageTransition,
                "This sub-stage cannot be started in its current state.");
        }

        subStage.ApplyStatusTransition(WorkStatus.Ongoing, today);
        stage.RecomputeStatusFromSubStages(today);
        RecomputeStatusFromStages();
    }

    public void FinishSubStage(int subStageId, DateTime today)
    {
        var (stage, subStage) = FindSubStage(subStageId);
        if (subStage.Status != WorkStatus.Ongoing)
        {
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.InvalidSubStageTransition,
                "Only an ongoing sub-stage can be finished.");
        }

        subStage.ApplyStatusTransition(WorkStatus.Finished, today);
        stage.RecomputeStatusFromSubStages(today);
        RecomputeStatusFromStages();
    }

    public void ResetSubStage(int subStageId, DateTime today)
    {
        var (stage, subStage) = FindSubStage(subStageId);
        if (subStage.Status != WorkStatus.Ongoing)
        {
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.InvalidSubStageTransition,
                "Only an ongoing sub-stage can be reset.");
        }

        subStage.ApplyStatusTransition(WorkStatus.NotStarted, today);
        stage.RecomputeStatusFromSubStages(today);
        RecomputeStatusFromStages();
    }

    public bool ContainsPaidWork()
        => Status == WorkStatus.Paid ||
           Stages.Any(stage =>
               stage.Status == WorkStatus.Paid ||
               stage.SubStages.Any(subStage => subStage.Status == WorkStatus.Paid));

    public int ResolveFinishedWorkPayment(DateTime today)
    {
        var resolved = 0;

        foreach (var stage in Stages)
        {
            foreach (var subStage in stage.SubStages.Where(x => x.Status == WorkStatus.Finished))
            {
                subStage.ApplyStatusTransition(WorkStatus.Paid, today);
                resolved++;
            }

            stage.RecomputeStatusFromSubStages(today);
        }

        RecomputeStatusFromStages();
        return resolved;
    }

    public void UpdateSubStageLaborCost(int subStageId, decimal laborCost)
    {
        var (_, subStage) = FindSubStage(subStageId);
        subStage.UpdateLaborCost(laborCost);
    }

    public void UpdateMaterialUsageQuantity(
        int subStageId,
        int materialUsageId,
        decimal quantity)
    {
        var (_, subStage) = FindSubStage(subStageId);
        var usage = subStage.MaterialUsages.FirstOrDefault(item => item.Id == materialUsageId);
        if (usage == null)
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.MaterialUsageNotFound,
                "The material usage was not found.");

        usage.UpdateQuantity(quantity);
    }

    public void StopUnfinishedWork(DateTime today)
    {
        foreach (var stage in Stages)
        {
            if (stage.Status is not (WorkStatus.Finished or WorkStatus.Paid))
                stage.ApplyStatusTransition(WorkStatus.Stopped, today);

            stage.RecomputeStatusFromSubStages(today);
        }

        Status = WorkStatus.Stopped;
    }

    public void AddMaterialUsage(
        int subStageId,
        int materialId,
        decimal quantity,
        DateTime usageDate)
    {
        var (_, subStage) = FindSubStage(subStageId);
        if (subStage.MaterialUsages.Any(usage => usage.MaterialId == materialId))
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.DuplicateMaterialUsage,
                "This material is already assigned to the sub-stage.");

        subStage.AddMaterialUsage(materialId, quantity, usageDate);
    }

    public void RemoveMaterialUsage(int subStageId, int materialUsageId)
    {
        var (_, subStage) = FindSubStage(subStageId);
        var usage = subStage.MaterialUsages.FirstOrDefault(item => item.Id == materialUsageId);
        if (usage == null)
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.MaterialUsageNotFound,
                "The material usage was not found.");

        subStage.MutableMaterialUsages.Remove(usage);
    }

    public SubStage AddSubStage(
        int stageId,
        string name,
        int desiredOrder,
        decimal laborCost)
    {
        var stage = Stages.FirstOrDefault(item => item.Id == stageId);
        if (stage == null)
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.StageNotFound,
                "The stage was not found.");

        var order = Math.Clamp(desiredOrder, 1, stage.SubStages.Count + 1);
        foreach (var sibling in stage.SubStages.Where(item => item.OrderIndex >= order))
            sibling.ShiftOrder(1);

        return stage.AddSubStage(name, order, laborCost);
    }

    public int RemoveSubStage(int subStageId, DateTime today)
    {
        var (stage, subStage) = FindSubStage(subStageId);
        var removedOrder = subStage.OrderIndex;
        stage.MutableSubStages.Remove(subStage);
        foreach (var sibling in stage.SubStages.Where(item => item.OrderIndex > removedOrder))
            sibling.ShiftOrder(-1);
        stage.RecomputeStatusFromSubStages(today);
        RecomputeStatusFromStages();
        return stage.Id;
    }

    public void RemoveStage(int stageId)
    {
        var stage = Stages.FirstOrDefault(item => item.Id == stageId);
        if (stage == null)
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.StageNotFound,
                "The stage was not found.");

        var removedOrder = stage.OrderIndex;
        _stages.Remove(stage);
        foreach (var sibling in Stages.Where(item => item.OrderIndex > removedOrder))
            sibling.ShiftOrder(-1);
        RecomputeStatusFromStages();
    }

    private (Stage Stage, SubStage SubStage) FindSubStage(int subStageId)
    {
        foreach (var stage in Stages)
        {
            var subStage = stage.SubStages.FirstOrDefault(x => x.Id == subStageId);
            if (subStage != null)
                return (stage, subStage);
        }

        throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
            Kanstraction.OnSite.Domain.Errors.DomainErrorCodes.SubStageNotFound,
            $"Sub-stage {subStageId} does not belong to building {Id}.");
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
