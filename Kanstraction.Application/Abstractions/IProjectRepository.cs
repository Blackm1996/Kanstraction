using Kanstraction.Domain.Entities;

namespace Kanstraction.Application.Abstractions;

public interface IProjectRepository
{
    Task<List<Project>> GetAllOrderedByNameAsync(CancellationToken cancellationToken = default);
    Task<Project?> GetByIdWithBuildingsAsync(int projectId, CancellationToken cancellationToken = default);
    Task AddAsync(Project project, CancellationToken cancellationToken = default);
    void Remove(Project project);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
