using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.OnSite.Infrastructure.Data;

public sealed class EfProjectRepository : IProjectRepository
{
    private readonly OnSiteDbContext _dbContext;

    public EfProjectRepository(OnSiteDbContext dbContext)
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

}
