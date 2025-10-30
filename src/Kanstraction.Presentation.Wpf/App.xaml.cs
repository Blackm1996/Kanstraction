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
using Kanstraction.Application.Reporting;
using Kanstraction.Application.Services;
using Kanstraction.Infrastructure.Services;
using Kanstraction.Presentation.Localization;
using Kanstraction.Shared.Localization;
using Kanstraction.Views;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanstraction;

public partial class App : global::System.Windows.Application
{
    private const string DefaultMaterialCategoryName = "Defaut";


    public static IBackupService BackupService { get; private set; } = null!;
    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        TextBoxEditHighlighter.Register();

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();

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

            loadingWindow.UpdateStatus("Application des migrations...");
            await using (var db = new AppDbContext())
            {
                await db.Database.MigrateAsync();

                /*if (databaseWasRecreated)
                {
                    DbSeeder.Seed(db);
                    await ImportMaterialsFromLatestBackupAsync(db);
                }*/
            }

            loadingWindow.UpdateStatus("Préparation des sauvegardes...");
            BackupService = Services.GetRequiredService<IBackupService>();
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

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<BackupService>();
        services.AddSingleton<IBackupService>(sp => sp.GetRequiredService<BackupService>());
        services.AddSingleton<IPaymentReportLocalizer, ResourcePaymentReportLocalizer>();
        services.AddTransient<IPaymentReportRenderer, PaymentReportRenderer>();
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
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
        command.CommandText = "SELECT \"Id\", \"StagePresetId\", \"Name\", \"OrderIndex\" FROM \"SubStagePresets\";";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var preset = new SubStagePreset
            {
                Id = Convert.ToInt32(reader.GetValue(0)),
                StagePresetId = Convert.ToInt32(reader.GetValue(1)),
                Name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                OrderIndex = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3))
            };

            results.Add(preset);
        }

        return results;
    }

    private static async Task<List<MaterialUsagePreset>> ReadLegacyMaterialUsagePresetsAsync(SqliteConnection connection)
    {
        var results = new List<MaterialUsagePreset>();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Id\", \"SubStagePresetId\", \"MaterialId\" FROM \"MaterialUsagesPreset\";";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var preset = new MaterialUsagePreset
            {
                Id = Convert.ToInt32(reader.GetValue(0)),
                SubStagePresetId = Convert.ToInt32(reader.GetValue(1)),
                MaterialId = Convert.ToInt32(reader.GetValue(2))
            };

            results.Add(preset);
        }

        return results;
    }

    private static async Task ImportMaterialsFromLatestBackupAsync(AppDbContext db)
    {
        try
        {
            var backupService = Services.GetRequiredService<IBackupService>();
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