using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.OnSite.Infrastructure.Data;

public sealed class EfConstructionRepository : IConstructionRepository
{
    private readonly OnSiteDbContext _dbContext;

    public EfConstructionRepository(OnSiteDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Building?> GetBuildingAggregateForStageStatusChangeAsync(int stageId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Buildings
            .Include(b => b.Stages)
                .ThenInclude(s => s.SubStages)
                    .ThenInclude(ss => ss.MaterialUsages)
            .FirstOrDefaultAsync(b => b.Stages.Any(s => s.Id == stageId), cancellationToken);
    }

    public Task<Building?> GetBuildingAggregateForSubStageAsync(
        int subStageId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Buildings
            .Include(building => building.Stages)
                .ThenInclude(stage => stage.SubStages)
                    .ThenInclude(subStage => subStage.MaterialUsages)
            .FirstOrDefaultAsync(
                building => building.Stages.Any(
                    stage => stage.SubStages.Any(subStage => subStage.Id == subStageId)),
                cancellationToken);
    }

    public async Task<IReadOnlyList<Building>> GetBuildingAggregatesForPaymentAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Buildings
            .Where(building => building.ProjectId == projectId)
            .Include(building => building.Stages)
                .ThenInclude(stage => stage.SubStages)
            .OrderBy(building => building.Code)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> BuildingCodeExistsAsync(
        int projectId,
        string code,
        CancellationToken cancellationToken = default)
        => _dbContext.Buildings.AnyAsync(
            building => building.ProjectId == projectId && building.Code == code,
            cancellationToken);

    public Task<Building?> GetBuildingAggregateAsync(
        int buildingId,
        CancellationToken cancellationToken = default)
        => _dbContext.Buildings
            .Include(building => building.Stages)
                .ThenInclude(stage => stage.SubStages)
            .FirstOrDefaultAsync(
                building => building.Id == buildingId,
                cancellationToken);

    public Task<Building?> GetBuildingAggregateForStopAsync(
        int buildingId,
        CancellationToken cancellationToken = default)
        => _dbContext.Buildings
            .Include(building => building.Stages)
                .ThenInclude(stage => stage.SubStages)
                    .ThenInclude(subStage => subStage.MaterialUsages)
                        .ThenInclude(usage => usage.Material)
                            .ThenInclude(material => material.PriceHistory)
            .FirstOrDefaultAsync(
                building => building.Id == buildingId,
                cancellationToken);

    public void Add(Building building)
        => _dbContext.Buildings.Add(building);

    public void Remove(Building building)
        => _dbContext.Buildings.Remove(building);
}
