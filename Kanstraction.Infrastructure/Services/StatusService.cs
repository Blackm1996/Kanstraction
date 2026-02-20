using Kanstraction.Domain.Entities;
using Kanstraction.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Infrastructure.Services;

public static class StatusService
{
    public static void SetSubStageStatus(SubStage subStage, WorkStatus newStatus, DateTime today)
        => subStage.ApplyStatusTransition(newStatus, today);

    public static void SetStageStatus(Stage stage, WorkStatus newStatus, DateTime today)
        => stage.ApplyStatusTransition(newStatus, today);

    public static async Task StopStageAsync(AppDbContext db, int stageId, DateTime today, CancellationToken ct = default)
    {
        var building = await db.Buildings
            .Include(b => b.Stages)
                .ThenInclude(s => s.SubStages)
                    .ThenInclude(ss => ss.MaterialUsages)
            .FirstAsync(b => b.Stages.Any(s => s.Id == stageId), ct);

        building.ChangeStageStatus(stageId, WorkStatus.Stopped, today);
        await db.SaveChangesAsync(ct);
    }

    public static async Task RecomputeBuildingStatusAsync(AppDbContext db, int buildingId, DateTime today, CancellationToken ct = default)
    {
        var building = await db.Buildings
            .Include(b => b.Stages)
                .ThenInclude(s => s.SubStages)
            .FirstAsync(b => b.Id == buildingId, ct);

        foreach (var stage in building.Stages)
            stage.RecomputeStatusFromSubStages(today);

        building.RecomputeStatusFromStages();
        await db.SaveChangesAsync(ct);
    }
}
