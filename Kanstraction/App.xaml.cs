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
    private const string DatabaseResetSentinelFileName = "app.reset"; // Delete this file beside app.db to trigger another rebuild on next launch.

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
            var databaseWasRecreated = EnsureDatabaseRecreatedOnce();

            loadingWindow.UpdateStatus("Application des migrations...");
            await using (var db = new AppDbContext())
            {
                await db.Database.MigrateAsync();

                if (databaseWasRecreated)
                {
                    DbSeeder.Seed(db);
                    await ImportMaterialsFromLatestBackupAsync(db);
                }
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
                    IsActive = isActive
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