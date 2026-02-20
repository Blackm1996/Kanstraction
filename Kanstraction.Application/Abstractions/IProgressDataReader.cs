using Kanstraction.Domain.Entities;

namespace Kanstraction.Application.Abstractions;

public interface IProgressDataReader
{
    Task<Stage?> GetStageAsync(int stageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Stage>> GetStagesAsync(IEnumerable<int> stageIds, CancellationToken cancellationToken = default);
    Task<Building?> GetBuildingAsync(int buildingId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Building>> GetBuildingsAsync(IEnumerable<int> buildingIds, CancellationToken cancellationToken = default);
}
