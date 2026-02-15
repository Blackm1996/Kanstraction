using Kanstraction.Application.Abstractions;
using Kanstraction.Domain.Entities;
using Kanstraction.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Infrastructure.Services;

public sealed class ProgressService : IProgressDataReader
{
    private readonly AppDbContext _db;

    public ProgressService(AppDbContext db)
    {
        _db = db;
    }

    public Task<Stage?> GetStageAsync(int stageId, CancellationToken cancellationToken = default)
    {
        return _db.Stages
            .Include(s => s.SubStages)
            .FirstOrDefaultAsync(s => s.Id == stageId, cancellationToken);
    }

    public async Task<IReadOnlyList<Stage>> GetStagesAsync(IEnumerable<int> stageIds, CancellationToken cancellationToken = default)
    {
        var ids = stageIds.Distinct().ToList();
        if (ids.Count == 0)
            return Array.Empty<Stage>();

        return await _db.Stages
            .Where(s => ids.Contains(s.Id))
            .Include(s => s.SubStages)
            .ToListAsync(cancellationToken);
    }

    public Task<Building?> GetBuildingAsync(int buildingId, CancellationToken cancellationToken = default)
    {
        return _db.Buildings
            .Include(b => b.Stages)
                .ThenInclude(s => s.SubStages)
            .FirstOrDefaultAsync(b => b.Id == buildingId, cancellationToken);
    }

    public async Task<IReadOnlyList<Building>> GetBuildingsAsync(IEnumerable<int> buildingIds, CancellationToken cancellationToken = default)
    {
        var ids = buildingIds.Distinct().ToList();
        if (ids.Count == 0)
            return Array.Empty<Building>();

        return await _db.Buildings
            .Where(b => ids.Contains(b.Id))
            .Include(b => b.Stages)
                .ThenInclude(s => s.SubStages)
            .ToListAsync(cancellationToken);
    }
}
