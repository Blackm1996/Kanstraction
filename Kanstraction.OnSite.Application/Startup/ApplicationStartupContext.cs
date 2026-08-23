using Kanstraction.OnSite.Application.Abstractions;

namespace Kanstraction.OnSite.Application.Startup;

public sealed class ApplicationStartupContext
{
    public ApplicationStartupContext(IBackupService backupService)
    {
        BackupService = backupService;
    }

    public IBackupService BackupService { get; }
}
