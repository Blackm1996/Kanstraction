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

        if (hasHistoryTable)
        {
            await using var baselineCheck = connection.CreateCommand();
            baselineCheck.CommandText = "SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = $id LIMIT 1;";
            baselineCheck.Parameters.AddWithValue("$id", BaselineMigrationId);
            var baselineExists = await baselineCheck.ExecuteScalarAsync() != null;
            if (baselineExists)
            {
                return;
            }
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

        await using var transaction = await connection.BeginTransactionAsync();
        var sqliteTransaction = (SqliteTransaction)transaction;

        if (!hasHistoryTable)
        {
            await using var createHistory = connection.CreateCommand();
            createHistory.Transaction = sqliteTransaction;
            createHistory.CommandText = "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL);";
            await createHistory.ExecuteNonQueryAsync();
        }

        await using (var insertBaseline = connection.CreateCommand())
        {
            insertBaseline.Transaction = sqliteTransaction;
            insertBaseline.CommandText = "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ($id, $version);";
            insertBaseline.Parameters.AddWithValue("$id", BaselineMigrationId);
            insertBaseline.Parameters.AddWithValue("$version", BaselineProductVersion);
            await insertBaseline.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}
