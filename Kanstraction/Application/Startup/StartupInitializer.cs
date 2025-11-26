using Kanstraction.Application.Abstractions;
using Kanstraction.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Application.Startup;

public class StartupInitializer
{
    private readonly Func<AppDbContext> _contextFactory;
    private readonly IBackupService _backupService;

    public StartupInitializer(Func<AppDbContext> contextFactory, IBackupService backupService)
    {
        _contextFactory = contextFactory;
        _backupService = backupService;
    }

    public async Task<ApplicationStartupContext> InitializeAsync()
    {
        await ApplyMigrationsAsync().ConfigureAwait(false);
        await _backupService.RunStartupMaintenanceAsync().ConfigureAwait(false);
        return new ApplicationStartupContext(_backupService);
    }

    private async Task ApplyMigrationsAsync()
    {
        await using var db = _contextFactory();
        await db.Database.MigrateAsync().ConfigureAwait(false);
    }
}
