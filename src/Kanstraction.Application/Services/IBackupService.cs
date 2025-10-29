using System.IO;

namespace Kanstraction.Application.Services;

public interface IBackupService
{
    string DatabasePath { get; }
    string StartupBackupsDirectory { get; }
    string HourlyBackupsDirectory { get; }

    Task RunStartupMaintenanceAsync();
    Task<string?> CreateStartupBackupAsync();
    Task<string?> CreateHourlyBackupAsync();
    Task<string?> CreateManualBackupAsync(string destinationPath);
    Task RestoreBackupAsync(string backupPath);
    FileInfo? GetLatestStartupBackup();
    FileInfo? GetLatestHourlyBackup();
}
