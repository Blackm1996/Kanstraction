using Kanstraction.Application.Abstractions;
using Kanstraction.Domain.Services;

namespace Kanstraction.Application.Services;

public sealed class ProgressQueryService : IProgressQueryService
{
    private readonly IProgressDataReader _progressDataReader;

    public ProgressQueryService(IProgressDataReader progressDataReader)
    {
        _progressDataReader = progressDataReader;
    }

    public async Task<double> ComputeStageProgressAsync(int stageId, CancellationToken cancellationToken = default)
    {
        var stage = await _progressDataReader.GetStageAsync(stageId, cancellationToken);
        return stage == null ? 0d : WorkProgressRules.ComputeStageProgress(stage);
    }

    public async Task<Dictionary<int, double>> ComputeStagesProgressAsync(IEnumerable<int> stageIds, CancellationToken cancellationToken = default)
    {
        var ids = stageIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, double>();

        var stages = await _progressDataReader.GetStagesAsync(ids, cancellationToken);
        var progress = stages.ToDictionary(s => s.Id, WorkProgressRules.ComputeStageProgress);

        foreach (var id in ids)
            if (!progress.ContainsKey(id))
                progress[id] = 0d;

        return progress;
    }

    public async Task<double> ComputeBuildingProgressAsync(int buildingId, CancellationToken cancellationToken = default)
    {
        var building = await _progressDataReader.GetBuildingAsync(buildingId, cancellationToken);
        return building == null ? 0d : WorkProgressRules.ComputeBuildingProgress(building);
    }

    public async Task<Dictionary<int, double>> ComputeBuildingsProgressAsync(IEnumerable<int> buildingIds, CancellationToken cancellationToken = default)
    {
        var ids = buildingIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, double>();

        var buildings = await _progressDataReader.GetBuildingsAsync(ids, cancellationToken);
        var progress = buildings.ToDictionary(b => b.Id, WorkProgressRules.ComputeBuildingProgress);

        foreach (var id in ids)
            if (!progress.ContainsKey(id))
                progress[id] = 0d;

        return progress;
    }
}
