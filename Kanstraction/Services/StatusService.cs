using Kanstraction.Data;
using Kanstraction.Entities;
using Kanstraction;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Services;

public static class StatusService
{
    // Validate and set a sub-stage status
    public static void SetSubStageStatus(SubStage ss, WorkStatus newStatus)
    {
        if (newStatus == WorkStatus.Finished || newStatus == WorkStatus.Paid)
        {
            // must have labor set (define your rule: > 0 or >= 0?)
            if (ss.LaborCost <= 0 && newStatus != WorkStatus.Stopped)
                throw new InvalidOperationException(ResourceHelper.GetString("StatusService_SetLaborFirst", "Set labor cost before marking as Finished/Paid."));
            if (newStatus == WorkStatus.Paid && ss.Status != WorkStatus.Finished)
                throw new InvalidOperationException(ResourceHelper.GetString("StatusService_SubStageMustBeFinished", "Sub-stage must be Finished before Paid."));
        }
        ss.Status = newStatus;
    }

    // Validate and set a stage status (checks children)
    public static void SetStageStatus(AppDbContext db, Stage s, WorkStatus newStatus)
    {
        if (newStatus == WorkStatus.Finished || newStatus == WorkStatus.Paid)
        {
            var subs = s.SubStages;
            if (subs.Count == 0)
                throw new InvalidOperationException(ResourceHelper.GetString("StatusService_NoSubStages", "Stage has no sub-stages; cannot mark as Finished."));

            var allDone = subs.All(x => x.Status == WorkStatus.Finished || x.Status == WorkStatus.Paid);
            if (!allDone)
                throw new InvalidOperationException(ResourceHelper.GetString("StatusService_AllSubStagesMustBeDone", "All sub-stages must be Finished/Paid."));
            if (newStatus == WorkStatus.Paid && s.Status != WorkStatus.Finished)
                throw new InvalidOperationException(ResourceHelper.GetString("StatusService_StageMustBeFinished", "Stage must be Finished before Paid."));
        }
        s.Status = newStatus;
    }

    // Stop a stage (and cascade)
    public static async Task StopStageAsync(AppDbContext db, int stageId)
    {
        var s = await db.Stages
            .Include(x => x.SubStages)
            .FirstAsync(x => x.Id == stageId);

        foreach (var ss in s.SubStages)
        {
            if (ss.Status == WorkStatus.NotStarted || ss.Status == WorkStatus.Ongoing)
                ss.Status = WorkStatus.Stopped;
        }
        s.Status = WorkStatus.Stopped;
        await db.SaveChangesAsync();

        await RecomputeBuildingStatusAsync(db, s.BuildingId);
    }

    // Recompute building from its stages
    public static async Task RecomputeBuildingStatusAsync(AppDbContext db, int buildingId)
    {
        var b = await db.Buildings
            .Include(x => x.Stages)
            .FirstAsync(x => x.Id == buildingId);

        if (b.Stages.Any(x => x.Status == WorkStatus.Ongoing))
            b.Status = WorkStatus.Ongoing;
        else if (b.Stages.All(x => x.Status == WorkStatus.Finished || x.Status == WorkStatus.Paid))
            b.Status = b.Stages.All(x => x.Status == WorkStatus.Paid) ? WorkStatus.Paid : WorkStatus.Finished;
        else if (b.Stages.Any(x => x.Status == WorkStatus.Stopped) && !b.Stages.Any(x => x.Status == WorkStatus.Ongoing))
            b.Status = WorkStatus.Stopped;
        else if (b.Stages.All(x => x.Status == WorkStatus.NotStarted))
            b.Status = WorkStatus.NotStarted;
        else
            b.Status = WorkStatus.NotStarted; // default fallback

        await db.SaveChangesAsync();
    }
}