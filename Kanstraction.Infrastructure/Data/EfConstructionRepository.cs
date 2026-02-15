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

    public Task<Stage?> GetStageForStatusChangeAsync(int stageId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Stages
            .Include(s => s.SubStages)
                .ThenInclude(ss => ss.MaterialUsages)
            .Include(s => s.Building)
                .ThenInclude(b => b.Stages)
                    .ThenInclude(st => st.SubStages)
                        .ThenInclude(ss => ss.MaterialUsages)
            .FirstOrDefaultAsync(s => s.Id == stageId, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _dbContext.SaveChangesAsync(cancellationToken);
}
