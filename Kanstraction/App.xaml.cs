using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kanstraction.Behaviors;
using Kanstraction.Data;
using Kanstraction.Services;
using Kanstraction.Views;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction;

public partial class App : Application
{
    private const string BaselineMigrationId = "20250924163704_BaselineExisting";
    private const string BaselineProductVersion = "9.0.8";

    public static BackupService BackupService { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        TextBoxEditHighlighter.Register();

        var culture = new CultureInfo("fr-FR");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Resources/StringResources.xaml", UriKind.Relative) });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Resources/StringResources.fr.xaml", UriKind.Relative) });

        var loadingWindow = new StartupLoadingWindow();
        loadingWindow.UpdateStatus("Initialisation de la base de données...");
        loadingWindow.Show();

        try
        {
            await EnsureLegacyDatabaseIsRegisteredAsync();

            loadingWindow.UpdateStatus("Application des migrations...");
            using (var db = new AppDbContext())
            {
                await db.Database.MigrateAsync();
            }

            loadingWindow.UpdateStatus("Préparation des sauvegardes...");
            BackupService = new BackupService();
            try
            {
                await BackupService.RunStartupMaintenanceAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Startup backup failed: {ex}");
            }

            loadingWindow.UpdateStatus("Démarrage de l'application...");
            if (loadingWindow.IsVisible)
            {
                loadingWindow.Close();
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Application startup failed: {ex}");

            if (loadingWindow.IsVisible)
            {
                loadingWindow.Close();
            }

            MessageBox.Show(
                $"Une erreur est survenue lors de l'initialisation de l'application.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Kanstraction",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
        finally
        {
            if (loadingWindow.IsVisible)
            {
                loadingWindow.Close();
            }
        }
    }

    private static async Task EnsureLegacyDatabaseIsRegisteredAsync()
    {
        var dbPath = AppDbContext.GetDefaultDbPath();
        if (!File.Exists(dbPath))
        {
            return;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = false
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var hasHistoryTable = false;
        await using (var historyCheck = connection.CreateCommand())
        {
            historyCheck.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory' LIMIT 1;";
            hasHistoryTable = await historyCheck.ExecuteScalarAsync() != null;
        }

        bool baselineExists;
        if (hasHistoryTable)
        {
            await using var baselineCheck = connection.CreateCommand();
            baselineCheck.CommandText = "SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = $id LIMIT 1;";
            baselineCheck.Parameters.AddWithValue("$id", BaselineMigrationId);
            baselineExists = await baselineCheck.ExecuteScalarAsync() != null;
        }
        else
        {
            baselineExists = false;
        }

        var hasExistingTables = false;
        await using (var tableCheck = connection.CreateCommand())
        {
            tableCheck.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' LIMIT 1;";
            hasExistingTables = await tableCheck.ExecuteScalarAsync() != null;
        }

        if (!hasExistingTables)
        {
            return;
        }

        var hasLockTable = false;
        await using (var lockCheck = connection.CreateCommand())
        {
            lockCheck.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsLock' LIMIT 1;";
            hasLockTable = await lockCheck.ExecuteScalarAsync() != null;
        }

        var lockTimestampIsNotNull = false;
        if (hasLockTable)
        {
            await using var lockInfo = connection.CreateCommand();
            lockInfo.CommandText = "PRAGMA table_info(\"__EFMigrationsLock\");";
            await using var reader = await lockInfo.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var columnName = reader.GetString(1);
                if (string.Equals(columnName, "Timestamp", StringComparison.OrdinalIgnoreCase))
                {
                    lockTimestampIsNotNull = reader.GetInt32(3) == 1;
                    break;
                }
            }
        }

        await using var transaction = await connection.BeginTransactionAsync();
        var sqliteTransaction = (SqliteTransaction)transaction;


        if (!hasHistoryTable)
        {
            await using var createHistory = connection.CreateCommand();

            createHistory.Transaction = sqliteTransaction;

            createHistory.CommandText = "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL);";
            await createHistory.ExecuteNonQueryAsync();
        }

        if (!baselineExists)
        {
            await using (var insertBaseline = connection.CreateCommand())
            {
                insertBaseline.Transaction = sqliteTransaction;

                insertBaseline.CommandText = "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ($id, $version);";
                insertBaseline.Parameters.AddWithValue("$id", BaselineMigrationId);
                insertBaseline.Parameters.AddWithValue("$version", BaselineProductVersion);
                await insertBaseline.ExecuteNonQueryAsync();
            }
        }

        if (!hasLockTable || lockTimestampIsNotNull)
        {
            if (lockTimestampIsNotNull)
            {
                await using (var dropOldLock = connection.CreateCommand())
                {
                    dropOldLock.Transaction = sqliteTransaction;
                    dropOldLock.CommandText = "DROP TABLE IF EXISTS \"__EFMigrationsLock_Old\";";
                    await dropOldLock.ExecuteNonQueryAsync();
                }

                await using (var renameLock = connection.CreateCommand())
                {
                    renameLock.Transaction = sqliteTransaction;
                    renameLock.CommandText = "ALTER TABLE \"__EFMigrationsLock\" RENAME TO \"__EFMigrationsLock_Old\";";
                    await renameLock.ExecuteNonQueryAsync();
                }
            }

            await using (var createLock = connection.CreateCommand())
            {
                createLock.Transaction = sqliteTransaction;
                createLock.CommandText = "CREATE TABLE IF NOT EXISTS \"__EFMigrationsLock\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK___EFMigrationsLock\" PRIMARY KEY, \"Timestamp\" TEXT NULL);";
                await createLock.ExecuteNonQueryAsync();
            }

            if (lockTimestampIsNotNull)
            {
                await using (var migrateLock = connection.CreateCommand())
                {
                    migrateLock.Transaction = sqliteTransaction;
                    migrateLock.CommandText = "INSERT OR IGNORE INTO \"__EFMigrationsLock\" (\"Id\", \"Timestamp\") SELECT \"Id\", NULL FROM \"__EFMigrationsLock_Old\";";
                    await migrateLock.ExecuteNonQueryAsync();
                }

                await using (var dropRenamedLock = connection.CreateCommand())
                {
                    dropRenamedLock.Transaction = sqliteTransaction;
                    dropRenamedLock.CommandText = "DROP TABLE IF EXISTS \"__EFMigrationsLock_Old\";";
                    await dropRenamedLock.ExecuteNonQueryAsync();
                }
            }
        }

        await using (var insertLock = connection.CreateCommand())
        {
            insertLock.Transaction = sqliteTransaction;
            insertLock.CommandText = "INSERT OR IGNORE INTO \"__EFMigrationsLock\" (\"Id\", \"Timestamp\") VALUES (1, NULL);";
            await insertLock.ExecuteNonQueryAsync();
        }

        await using (var resetLock = connection.CreateCommand())
        {
            resetLock.Transaction = sqliteTransaction;
            resetLock.CommandText = "UPDATE \"__EFMigrationsLock\" SET \"Timestamp\" = NULL WHERE \"Id\" = 1;";
            await resetLock.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}