using Kanstraction.Application.Abstractions;
using Kanstraction.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Infrastructure.Data;

public sealed class EfConstructionRepository : IConstructionRepository
{
    private readonly AppDbContext _dbContext;

    public EfConstructionRepository(AppDbContext dbContext)
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

    public Task SaveBuildingAggregateAsync(Building building, CancellationToken cancellationToken = default)
        => _dbContext.SaveChangesAsync(cancellationToken);
}
