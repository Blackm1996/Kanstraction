using Kanstraction.Application.Abstractions;

namespace Kanstraction.Application.Startup;

public class StartupInitializer
{
    private readonly IDatabaseMigrator _databaseMigrator;
    private readonly IBackupService _backupService;

    public StartupInitializer(IDatabaseMigrator databaseMigrator, IBackupService backupService)
    {
        _databaseMigrator = databaseMigrator;
        _backupService = backupService;
    }

    public async Task<ApplicationStartupContext> InitializeAsync()
    {
        await _databaseMigrator.ApplyMigrationsAsync().ConfigureAwait(false);
        await _backupService.RunStartupMaintenanceAsync().ConfigureAwait(false);
        return new ApplicationStartupContext(_backupService);
    }
}
