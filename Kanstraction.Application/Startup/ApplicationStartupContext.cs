using Kanstraction.Application.Abstractions;

namespace Kanstraction.Application.Startup;

public sealed class ApplicationStartupContext
{
    public ApplicationStartupContext(IBackupService backupService)
    {
        BackupService = backupService;
    }

    public IBackupService BackupService { get; }
}
