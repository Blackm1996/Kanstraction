using Kanstraction.Domain.Entities;

namespace Kanstraction.Domain.Services;

public static class WorkProgressRules
{
    public static double StatusToCompletion(WorkStatus status) => status switch
    {
        WorkStatus.Finished => 1d,
        WorkStatus.Paid => 1d,
        WorkStatus.Stopped => 1d,
        _ => 0d
    };

    public static double ComputeStageProgress(Stage stage)
    {
        if (stage.SubStages.Count == 0)
            return StatusToCompletion(stage.Status);

        return stage.SubStages
            .OrderBy(ss => ss.OrderIndex)
            .Select(ss => StatusToCompletion(ss.Status))
            .DefaultIfEmpty(0d)
            .Average();
    }

    public static double ComputeBuildingProgress(Building building)
    {
        if (building.Stages.Count == 0)
            return StatusToCompletion(building.Status);

        return building.Stages
            .OrderBy(s => s.OrderIndex)
            .Select(ComputeStageProgress)
            .DefaultIfEmpty(0d)
            .Average();
    }
}
