using Kanstraction.OnSite.Domain.Entities;

namespace Kanstraction.OnSite.Application.Abstractions;

public interface IConstructionRepository
{
    Task<Building?> GetBuildingAggregateForStageStatusChangeAsync(int stageId, CancellationToken cancellationToken = default);
    Task<Building?> GetBuildingAggregateForSubStageAsync(int subStageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Building>> GetBuildingAggregatesForPaymentAsync(
        int projectId,
        CancellationToken cancellationToken = default);
    Task<bool> BuildingCodeExistsAsync(
        int projectId,
        string code,
        CancellationToken cancellationToken = default);
    Task<Building?> GetBuildingAggregateAsync(
        int buildingId,
        CancellationToken cancellationToken = default);
    Task<Building?> GetBuildingAggregateForStopAsync(
        int buildingId,
        CancellationToken cancellationToken = default);
    void Add(Building building);
    void Remove(Building building);
}
