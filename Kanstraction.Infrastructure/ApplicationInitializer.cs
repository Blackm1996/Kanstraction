using Kanstraction.Application.Common;
using Kanstraction.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Infrastructure;

internal sealed class ApplicationInitializer : IApplicationInitializer
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public ApplicationInitializer(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
        DbSeeder.Seed(dbContext);
    }
}
