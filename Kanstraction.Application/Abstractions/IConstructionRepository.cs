using Kanstraction.Domain.Entities;

namespace Kanstraction.Application.Abstractions;

public interface IConstructionRepository
{
    Task<Stage?> GetStageForStatusChangeAsync(int stageId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
