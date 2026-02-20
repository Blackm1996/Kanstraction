namespace Kanstraction.Application.Abstractions;

public interface IProgressQueryService
{
    Task<double> ComputeStageProgressAsync(int stageId, CancellationToken cancellationToken = default);
    Task<Dictionary<int, double>> ComputeStagesProgressAsync(IEnumerable<int> stageIds, CancellationToken cancellationToken = default);
    Task<double> ComputeBuildingProgressAsync(int buildingId, CancellationToken cancellationToken = default);
    Task<Dictionary<int, double>> ComputeBuildingsProgressAsync(IEnumerable<int> buildingIds, CancellationToken cancellationToken = default);
}
