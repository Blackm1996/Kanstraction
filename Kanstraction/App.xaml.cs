using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kanstraction.Behaviors;
using Kanstraction.Data;
using Kanstraction.Entities;
using Kanstraction.Services;
using Kanstraction.Views;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction;

public partial class App : Application
{
    private const string DatabaseResetSentinelFileName = "app2.reset"; // Delete this file beside app.db to trigger another rebuild on next launch.
    private const string LegacyImportSentinelFileName = "client-backup2.imported";
    private const string LegacyBackupFilePath = "KanstractionBackup_20250926_185305.db"; // Expected beside Kanstraction.exe unless overridden with an absolute path.
    private const string DefaultMaterialCategoryName = "Defaut";


    public static BackupService BackupService { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        TextBoxEditHighlighter.Register();

        var culture = new CultureInfo("fr-FR");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Resources/StringResources.xaml", UriKind.Relative) });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Resources/StringResources.fr.xaml", UriKind.Relative) });
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var loadingWindow = new StartupLoadingWindow();
        loadingWindow.UpdateStatus("Initialisation de la base de données...");
        loadingWindow.Show();

        try
        {
            var databaseWasRecreated = EnsureDatabaseRecreatedOnce();

            loadingWindow.UpdateStatus("Application des migrations...");
            await using (var db = new AppDbContext())
            {
                await db.Database.MigrateAsync();

                await ImportLegacyDataFromClientBackupAsync(db, databaseWasRecreated);

                /*if (databaseWasRecreated)
                {
                    DbSeeder.Seed(db);
                    await ImportMaterialsFromLatestBackupAsync(db);
                }*/
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
            ShutdownMode = ShutdownMode.OnMainWindowClose;
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

    private static bool EnsureDatabaseRecreatedOnce()
    {
        var dbPath = AppDbContext.GetDefaultDbPath();
        var dbDirectory = Path.GetDirectoryName(dbPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(dbDirectory);

        var sentinelPath = Path.Combine(dbDirectory, DatabaseResetSentinelFileName);

        if (File.Exists(sentinelPath))
        {
            Debug.WriteLine($"Database reset sentinel present at '{sentinelPath}'. Skipping destructive reset.");
            return false;
        }

        Debug.WriteLine($"Database reset sentinel missing at '{sentinelPath}'. Resetting database located at '{dbPath}'.");

        TryDeleteFile(dbPath);
        TryDeleteFile($"{dbPath}-wal");
        TryDeleteFile($"{dbPath}-shm");

        File.WriteAllText(sentinelPath, $"Reset performed at {DateTimeOffset.Now:O}");

        return true;
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static async Task ImportLegacyDataFromClientBackupAsync(AppDbContext db, bool databaseWasRecreated)
    {
        try
        {
            if (!databaseWasRecreated)
            {
                Debug.WriteLine("Legacy import skipped because database was not recreated.");
                return;
            }

            var dbPath = AppDbContext.GetDefaultDbPath();
            var dbDirectory = Path.GetDirectoryName(dbPath) ?? Directory.GetCurrentDirectory();
            var sentinelPath = Path.Combine(dbDirectory, LegacyImportSentinelFileName);

            if (File.Exists(sentinelPath))
            {
                Debug.WriteLine($"Legacy import sentinel present at '{sentinelPath}'. Skipping legacy import.");
                return;
            }

            var legacyDbPath = ResolveLegacyBackupFilePath(dbDirectory);
            if (legacyDbPath == null)
            {
                Debug.WriteLine("No legacy client backup located for import.");
                return;
            }

            if (await db.Materials.AnyAsync() || await db.StagePresets.AnyAsync() || await db.SubStagePresets.AnyAsync() || await db.MaterialUsagesPreset.AnyAsync() || await db.MaterialPriceHistory.AnyAsync())
            {
                Debug.WriteLine("Legacy import skipped because target tables already contain data.");
                return;
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = legacyDbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var materials = await ReadLegacyMaterialsAsync(connection);
            var materialPriceHistory = await ReadLegacyMaterialPriceHistoryAsync(connection);
            var stagePresets = await ReadLegacyStagePresetsAsync(connection);
            var subStagePresets = await ReadLegacySubStagePresetsAsync(connection);
            var materialUsagePresets = await ReadLegacyMaterialUsagePresetsAsync(connection);

            if (materials.Count == 0 && materialPriceHistory.Count == 0 && stagePresets.Count == 0 && subStagePresets.Count == 0 && materialUsagePresets.Count == 0)
            {
                Debug.WriteLine($"Legacy database '{legacyDbPath}' did not contain any relevant data to import.");
                return;
            }

            await using var transaction = await db.Database.BeginTransactionAsync();

            var defaultCategory = await EnsureDefaultMaterialCategoryAsync(db);

            if (materials.Count > 0)
            {
                foreach (var material in materials)
                {
                    material.MaterialCategoryId = defaultCategory.Id;
                }

                await db.Materials.AddRangeAsync(materials);
                await db.SaveChangesAsync();
            }

            if (stagePresets.Count > 0)
            {
                await db.StagePresets.AddRangeAsync(stagePresets);
                await db.SaveChangesAsync();
            }

            if (subStagePresets.Count > 0)
            {
                await db.SubStagePresets.AddRangeAsync(subStagePresets);
                await db.SaveChangesAsync();
            }

            if (materialPriceHistory.Count > 0)
            {
                await db.MaterialPriceHistory.AddRangeAsync(materialPriceHistory);
                await db.SaveChangesAsync();
            }

            if (materialUsagePresets.Count > 0)
            {
                await db.MaterialUsagesPreset.AddRangeAsync(materialUsagePresets);
                await db.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            File.WriteAllText(sentinelPath, $"Imported from '{legacyDbPath}' at {DateTimeOffset.Now:O}");
            Debug.WriteLine($"Legacy client data imported successfully from '{legacyDbPath}'.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to import legacy client data: {ex}");
        }
    }

    private static async Task<MaterialCategory> EnsureDefaultMaterialCategoryAsync(AppDbContext db)
    {
        var existing = await db.MaterialCategories.FirstOrDefaultAsync(c => c.Name == DefaultMaterialCategoryName);
        if (existing != null)
        {
            return existing;
        }

        var category = new MaterialCategory
        {
            Name = DefaultMaterialCategoryName
        };

        db.MaterialCategories.Add(category);
        await db.SaveChangesAsync();

        return category;
    }

    private static string? ResolveLegacyBackupFilePath(string dbDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(LegacyBackupFilePath))
            {
                Debug.WriteLine("Legacy backup file path not configured.");
                return null;
            }

            if (Path.IsPathRooted(LegacyBackupFilePath))
            {
                return File.Exists(LegacyBackupFilePath) ? LegacyBackupFilePath : null;
            }

            var candidateDirectories = new List<string>
            {
                AppContext.BaseDirectory,
                dbDirectory,
                Directory.GetCurrentDirectory()
            };

            var searchedLocations = new List<string>();

            foreach (var directory in candidateDirectories.Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                var candidatePath = Path.Combine(directory, LegacyBackupFilePath);
                searchedLocations.Add(candidatePath);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            Debug.WriteLine(
                $"Legacy backup file '{LegacyBackupFilePath}' not found. Looked in: {string.Join(", ", searchedLocations)}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to locate legacy backup file: {ex}");
        }

        return null;
    }

    private static async Task<List<Material>> ReadLegacyMaterialsAsync(SqliteConnection connection)
    {
        var results = new List<Material>();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Id\", \"Name\", \"Unit\", \"PricePerUnit\", \"EffectiveSince\", \"IsActive\" FROM \"Materials\";";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var material = new Material
            {
                Id = Convert.ToInt32(reader.GetValue(0)),
                Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Unit = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                PricePerUnit = reader.IsDBNull(3) ? 0m : Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture),
                EffectiveSince = reader.IsDBNull(4) ? DateTime.Today : ConvertToDateTime(reader.GetValue(4)),
                IsActive = reader.IsDBNull(5) || ConvertToBoolean(reader.GetValue(5))
            };

            results.Add(material);
        }

        return results;
    }

    private static async Task<List<MaterialPriceHistory>> ReadLegacyMaterialPriceHistoryAsync(SqliteConnection connection)
    {
        var results = new List<MaterialPriceHistory>();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Id\", \"MaterialId\", \"PricePerUnit\", \"StartDate\", \"EndDate\" FROM \"MaterialPriceHistory\";";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var history = new MaterialPriceHistory
            {
                Id = Convert.ToInt32(reader.GetValue(0)),
                MaterialId = Convert.ToInt32(reader.GetValue(1)),
                PricePerUnit = reader.IsDBNull(2) ? 0m : Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture),
                StartDate = reader.IsDBNull(3) ? DateTime.Today : ConvertToDateTime(reader.GetValue(3)),
                EndDate = reader.IsDBNull(4) ? null : ConvertToDateTime(reader.GetValue(4))
            };

            results.Add(history);
        }

        return results;
    }

    private static async Task<List<StagePreset>> ReadLegacyStagePresetsAsync(SqliteConnection connection)
    {
        var results = new List<StagePreset>();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Id\", \"Name\", \"IsActive\" FROM \"StagePresets\";";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var preset = new StagePreset
            {
                Id = Convert.ToInt32(reader.GetValue(0)),
                Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                IsActive = reader.IsDBNull(2) || ConvertToBoolean(reader.GetValue(2))
            };

            results.Add(preset);
        }

        return results;
    }

    private static async Task<List<SubStagePreset>> ReadLegacySubStagePresetsAsync(SqliteConnection connection)
    {
        var results = new List<SubStagePreset>();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Id\", \"StagePresetId\", \"Name\", \"OrderIndex\", \"LaborCost\" FROM \"SubStagePresets\";";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var preset = new SubStagePreset
            {
                Id = Convert.ToInt32(reader.GetValue(0)),
                StagePresetId = Convert.ToInt32(reader.GetValue(1)),
                Name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                OrderIndex = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)),
                LaborCost = reader.IsDBNull(4)
                    ? null
                    : NormalizeLegacyLaborCost(Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture))
            };

            results.Add(preset);
        }

        return results;
    }

    private static async Task<List<MaterialUsagePreset>> ReadLegacyMaterialUsagePresetsAsync(SqliteConnection connection)
    {
        var results = new List<MaterialUsagePreset>();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Id\", \"SubStagePresetId\", \"MaterialId\", \"Qty\" FROM \"MaterialUsagesPreset\";";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var preset = new MaterialUsagePreset
            {
                Id = Convert.ToInt32(reader.GetValue(0)),
                SubStagePresetId = Convert.ToInt32(reader.GetValue(1)),
                MaterialId = Convert.ToInt32(reader.GetValue(2)),
                Qty = reader.IsDBNull(3) ? (decimal?)null : Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture)
            };

            results.Add(preset);
        }

        return results;
    }

    private static async Task ImportMaterialsFromLatestBackupAsync(AppDbContext db)
    {
        try
        {
            var backupService = new BackupService();
            var backupFile = backupService.GetLatestStartupBackup() ?? backupService.GetLatestHourlyBackup();

            if (backupFile == null || !backupFile.Exists)
            {
                Debug.WriteLine("No backup available for material import after database reset.");
                return;
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = backupFile.FullName,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT \"Name\", \"Unit\", \"PricePerUnit\", \"EffectiveSince\", \"IsActive\" FROM \"Materials\";";

            await using var reader = await command.ExecuteReaderAsync();

            var existingByName = (await db.Materials.AsNoTracking().ToListAsync())
                .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var imported = new List<Material>();
            var defaultCategory = await EnsureDefaultMaterialCategoryAsync(db);

            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                if (existingByName.ContainsKey(name))
                {
                    continue;
                }

                var unit = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var price = reader.IsDBNull(2) ? 0m : Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture);
                var effectiveSince = reader.IsDBNull(3) ? DateTime.Today : ConvertToDateTime(reader.GetValue(3));
                var isActive = reader.IsDBNull(4) || ConvertToBoolean(reader.GetValue(4));

                imported.Add(new Material
                {
                    Name = name,
                    Unit = unit,
                    PricePerUnit = price,
                    EffectiveSince = effectiveSince,
                    IsActive = isActive,
                    MaterialCategoryId = defaultCategory.Id
                });
            }

            if (imported.Count == 0)
            {
                Debug.WriteLine("No new materials found in backup to import.");
                return;
            }

            await db.Materials.AddRangeAsync(imported);
            await db.SaveChangesAsync();

            Debug.WriteLine($"Imported {imported.Count} material(s) from backup '{backupFile.FullName}'.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to import materials from backup: {ex}");
        }
    }

    private static DateTime ConvertToDateTime(object value)
    {
        return value switch
        {
            DateTime dt => dt,
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) => parsed,
            string s when DateTime.TryParse(s, out var parsed) => parsed,
            long ticks => DateTime.FromBinary(ticks),
            _ => DateTime.Today
        };
    }

    private static decimal? NormalizeLegacyLaborCost(decimal value)
    {
        return value == 0m ? null : value;
    }

    private static bool ConvertToBoolean(object value)
    {
        return value switch
        {
            bool b => b,
            long l => l != 0,
            int i => i != 0,
            short s => s != 0,
            byte bt => bt != 0,
            string str when bool.TryParse(str, out var parsed) => parsed,
            _ => false
        };
    }
}