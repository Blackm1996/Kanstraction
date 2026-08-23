using Kanstraction.OnSite.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.OnSite.Infrastructure.Data;

public class DatabaseMigrator : IDatabaseMigrator
{
    private readonly IDbContextFactory<OnSiteDbContext> _contextFactory;

    public DatabaseMigrator(IDbContextFactory<OnSiteDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task ApplyMigrationsAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await db.Database.MigrateAsync().ConfigureAwait(false);
    }
}
