using Kanstraction.Data;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kanstraction.Services;

public class BackupService
{
    private const string StartupPrefix = "startup";
    private const string HourlyPrefix = "hourly";

    private readonly string _dbPath;
    private readonly string _rootBackupDir;
    private readonly string _startupBackupDir;
    private readonly string _hourlyBackupDir;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public BackupService()
    {
        _dbPath = AppDbContext.GetDefaultDbPath();
        var dbDirectory = Path.GetDirectoryName(_dbPath) ?? Directory.GetCurrentDirectory();
        _rootBackupDir = Path.Combine(dbDirectory, "Backups");
        _startupBackupDir = Path.Combine(_rootBackupDir, "Startup");
        _hourlyBackupDir = Path.Combine(_rootBackupDir, "Hourly");
    }

    public string DatabasePath => _dbPath;
    public string StartupBackupsDirectory => _startupBackupDir;
    public string HourlyBackupsDirectory => _hourlyBackupDir;

    public async Task RunStartupMaintenanceAsync()
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            CleanupStartupBackupsInternal();
            CleanupHourlyBackupsInternal();
            await CreateAutomaticBackupInternalAsync(_startupBackupDir, StartupPrefix).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<string?> CreateStartupBackupAsync()
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return await CreateAutomaticBackupInternalAsync(_startupBackupDir, StartupPrefix).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<string?> CreateHourlyBackupAsync()
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return await CreateAutomaticBackupInternalAsync(_hourlyBackupDir, HourlyPrefix).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<string?> CreateManualBackupAsync(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path is required.", nameof(destinationPath));

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_dbPath))
                throw new FileNotFoundException("Database file not found.", _dbPath);

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            await PerformSqliteBackupAsync(_dbPath, destinationPath).ConfigureAwait(false);
            return destinationPath;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task RestoreBackupAsync(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
            throw new ArgumentException("Backup path is required.", nameof(backupPath));

        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file not found.", backupPath);

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var destinationDirectory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            await PerformSqliteBackupAsync(backupPath, _dbPath).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public FileInfo? GetLatestStartupBackup() => GetLatestBackup(_startupBackupDir, StartupPrefix);
    public FileInfo? GetLatestHourlyBackup() => GetLatestBackup(_hourlyBackupDir, HourlyPrefix);

    private async Task<string?> CreateAutomaticBackupInternalAsync(string directory, string prefix)
    {
        if (!File.Exists(_dbPath))
            return null;

        Directory.CreateDirectory(directory);
        var fileName = $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.db";
        var destination = Path.Combine(directory, fileName);
        await PerformSqliteBackupAsync(_dbPath, destination).ConfigureAwait(false);
        return destination;
    }

    private static FileInfo? GetLatestBackup(string directory, string prefix)
    {
        if (!Directory.Exists(directory))
            return null;

        return Directory.EnumerateFiles(directory, $"{prefix}_*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private async Task PerformSqliteBackupAsync(string sourcePath, string destinationPath)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var destinationFullPath = Path.GetFullPath(destinationPath);

        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source and destination must be different.");

        await Task.Run(() =>
        {
            var destinationDirectory = Path.GetDirectoryName(destinationFullPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            if (File.Exists(destinationFullPath))
                File.Delete(destinationFullPath);

            var sourceConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = sourceFullPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();

            var destinationConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = destinationFullPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();

            SqliteConnection? source = null;
            SqliteConnection? destination = null;

            try
            {
                source = new SqliteConnection(sourceConnectionString);
                source.Open();

                destination = new SqliteConnection(destinationConnectionString);
                destination.Open();

                source.BackupDatabase(destination);
            }
            finally
            {
                destination?.Dispose();
                source?.Dispose();

                TryDeleteFile($"{destinationFullPath}-wal");
                TryDeleteFile($"{destinationFullPath}-shm");

                if (!string.Equals(sourceFullPath, _dbPath, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile($"{sourceFullPath}-wal");
                    TryDeleteFile($"{sourceFullPath}-shm");
                }
            }
        }).ConfigureAwait(false);
    }

    private void CleanupStartupBackupsInternal()
    {
        if (!Directory.Exists(_startupBackupDir))
            return;

        var today = DateTime.Today;

        var backups = Directory.EnumerateFiles(_startupBackupDir, $"{StartupPrefix}_*.db")
            .Select(path => new
            {
                Path = path,
                Timestamp = TryParseTimestamp(Path.GetFileName(path)!, StartupPrefix)
            })
            .Where(x => x.Timestamp != null)
            .Select(x => new { x.Path, Timestamp = x.Timestamp!.Value })
            .ToList();

        var groupsByDay = backups
            .GroupBy(x => x.Timestamp.Date)
            .OrderByDescending(group => group.Key)
            .ToList();

        foreach (var group in groupsByDay)
        {
            if (group.Key == today)
                continue;

            var latest = group.OrderByDescending(x => x.Timestamp).First();
            foreach (var entry in group)
            {
                if (!ReferenceEquals(entry, latest))
                    TryDeleteFile(entry.Path);
            }
        }

        var oldGroups = groupsByDay
            .Where(g => g.Key < today)
            .OrderByDescending(g => g.Key)
            .Skip(7);

        foreach (var group in oldGroups)
        {
            foreach (var entry in group)
                TryDeleteFile(entry.Path);
        }
    }

    private void CleanupHourlyBackupsInternal()
    {
        if (!Directory.Exists(_hourlyBackupDir))
            return;

        var cutoffDate = DateTime.Today.AddDays(-2);

        foreach (var path in Directory.EnumerateFiles(_hourlyBackupDir, $"{HourlyPrefix}_*.db"))
        {
            var timestamp = TryParseTimestamp(Path.GetFileName(path)!, HourlyPrefix);
            var date = timestamp?.Date ?? File.GetLastWriteTime(path).Date;
            if (date <= cutoffDate)
                TryDeleteFile(path);
        }
    }

    private static DateTime? TryParseTimestamp(string fileName, string prefix)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var expectedPrefix = $"{prefix}_";
        if (!fileName.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (nameWithoutExtension.Length <= expectedPrefix.Length)
            return null;

        var timestampPart = nameWithoutExtension.Substring(expectedPrefix.Length);
        if (DateTime.TryParseExact(timestampPart, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            return timestamp;

        return null;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
