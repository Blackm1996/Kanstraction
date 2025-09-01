using Kanstraction.Data;
using Kanstraction.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Services;

public static class ProgressService
{
    // Your mapping: NS=0, Ongoing=0, Finished/Paid/Stopped=1
    public static double StatusToCompletion(WorkStatus s) => s switch
    {
        WorkStatus.NotStarted => 0.0,
        WorkStatus.Ongoing => 0.0,
        WorkStatus.Finished => 1.0,
        WorkStatus.Paid => 1.0,
        WorkStatus.Stopped => 1.0,
        _ => 0.0
    };

    // Stage %: average of sub-stages (if any); else status
    public static async Task<double> ComputeStageProgressAsync(AppDbContext db, int stageId, CancellationToken ct = default)
    {
        var subs = await db.SubStages
            .Where(x => x.StageId == stageId)
            .Select(x => x.Status)
            .ToListAsync(ct);

        if (subs.Count == 0)
        {
            var status = await db.Stages.Where(x => x.Id == stageId)
                                        .Select(x => x.Status)
                                        .SingleAsync(ct);
            return StatusToCompletion(status);
        }

        return subs.Select(StatusToCompletion).DefaultIfEmpty(0.0).Average();
    }

    // Batch stages progress
    public static async Task<Dictionary<int, double>> ComputeStagesProgressAsync(AppDbContext db, IEnumerable<int> stageIds, CancellationToken ct = default)
    {
        var ids = stageIds.Distinct().ToList();
        if (ids.Count == 0) return new();

        // Load all sub-stages for those stages
        var subs = await db.SubStages
            .Where(ss => ids.Contains(ss.StageId))
            .Select(ss => new { ss.StageId, ss.Status })
            .ToListAsync(ct);

        var byStage = subs.GroupBy(x => x.StageId).ToDictionary(g => g.Key, g => g.Select(v => v.Status).ToList());

        // Stages without subs → use their own status
        var withoutSubs = ids.Except(byStage.Keys).ToList();
        var result = new Dictionary<int, double>();

        foreach (var kv in byStage)
            result[kv.Key] = kv.Value.Select(StatusToCompletion).DefaultIfEmpty(0.0).Average();

        if (withoutSubs.Count > 0)
        {
            var statuses = await db.Stages
                .Where(s => withoutSubs.Contains(s.Id))
                .Select(s => new { s.Id, s.Status })
                .ToListAsync(ct);

            foreach (var s in statuses)
                result[s.Id] = StatusToCompletion(s.Status);
        }

        return result;
    }

    // Building %: equal-weight average of its stages
    public static async Task<double> ComputeBuildingProgressAsync(AppDbContext db, int buildingId, CancellationToken ct = default)
    {
        var stageIds = await db.Stages
            .Where(s => s.BuildingId == buildingId)
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (stageIds.Count == 0) return 0.0;

        var stageProgress = await ComputeStagesProgressAsync(db, stageIds, ct);
        return stageProgress.Values.DefaultIfEmpty(0.0).Average();
    }

    // Batch buildings %
    public static async Task<Dictionary<int, double>> ComputeBuildingsProgressAsync(AppDbContext db, IEnumerable<int> buildingIds, CancellationToken ct = default)
    {
        var bIds = buildingIds.Distinct().ToList();
        if (bIds.Count == 0) return new();

        var stages = await db.Stages
            .Where(s => bIds.Contains(s.BuildingId))
            .Select(s => new { s.Id, s.BuildingId })
            .ToListAsync(ct);

        if (stages.Count == 0)
            return bIds.ToDictionary(id => id, _ => 0.0);

        var stageProgress = await ComputeStagesProgressAsync(db, stages.Select(s => s.Id), ct);

        var byBuilding = stages
            .GroupBy(s => s.BuildingId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(s => stageProgress[s.Id]).DefaultIfEmpty(0.0).Average()
            );

        // Ensure all buildings present
        foreach (var id in bIds)
            if (!byBuilding.ContainsKey(id)) byBuilding[id] = 0.0;

        return byBuilding;
    }
}