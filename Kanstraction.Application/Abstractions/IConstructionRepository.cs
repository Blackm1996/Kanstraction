using Kanstraction.Domain.Entities;

namespace Kanstraction.Application.Abstractions;

public interface IConstructionRepository
{
    Task<Building?> GetBuildingAggregateForStageStatusChangeAsync(int stageId, CancellationToken cancellationToken = default);
    Task SaveBuildingAggregateAsync(Building building, CancellationToken cancellationToken = default);
}
