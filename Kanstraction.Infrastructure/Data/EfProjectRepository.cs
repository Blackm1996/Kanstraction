using Kanstraction.Application.Abstractions;
using Kanstraction.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Infrastructure.Data;

public sealed class EfProjectRepository : IProjectRepository
{
    private readonly AppDbContext _dbContext;

    public EfProjectRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<Project>> GetAllOrderedByNameAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Projects
            .AsNoTracking()
            .OrderBy(project => project.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Project?> GetByIdWithBuildingsAsync(int projectId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Projects
            .Include(project => project.Buildings)
            .FirstOrDefaultAsync(project => project.Id == projectId, cancellationToken);
    }

    public Task AddAsync(Project project, CancellationToken cancellationToken = default)
        => _dbContext.Projects.AddAsync(project, cancellationToken).AsTask();

    public void Remove(Project project)
        => _dbContext.Projects.Remove(project);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _dbContext.SaveChangesAsync(cancellationToken);
}
