using Kanstraction.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Infrastructure.Data;

public class DatabaseMigrator : IDatabaseMigrator
{
    private readonly Func<AppDbContext> _contextFactory;

    public DatabaseMigrator(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task ApplyMigrationsAsync()
    {
        await using var db = _contextFactory();
        await db.Database.MigrateAsync().ConfigureAwait(false);
    }
}
