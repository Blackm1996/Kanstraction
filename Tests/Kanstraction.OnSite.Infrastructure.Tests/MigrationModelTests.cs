using Kanstraction.OnSite.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kanstraction.OnSite.Infrastructure.Tests;

public sealed class MigrationModelTests
{
    [Fact]
    public async Task All_migrations_apply_to_a_new_sqlite_database()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OnSiteDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new OnSiteDbContext(options);

        await db.Database.MigrateAsync();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains("20260730000000_AddBuildingCodeUniqueness", applied);
    }

    [Fact]
    public void Current_model_matches_the_latest_migration_snapshot()
    {
        var options = new DbContextOptionsBuilder<OnSiteDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new OnSiteDbContext(options);

        var snapshotType = typeof(OnSiteDbContext).Assembly
            .GetType("Kanstraction.Migrations.OnSiteDbContextModelSnapshot", throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
        var currentModel = db.GetService<IDesignTimeModel>().Model;
        var modelDiffer = db.GetService<IMigrationsModelDiffer>();
        var snapshotModel = db.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot.Model, designTime: true);

        var differences = modelDiffer.GetDifferences(
            snapshotModel.GetRelationalModel(),
            currentModel.GetRelationalModel());

        Assert.True(
            differences.Count == 0,
            "Pending migration operations: " +
            string.Join(", ", differences.Select(operation => operation.GetType().Name)));
    }
}
