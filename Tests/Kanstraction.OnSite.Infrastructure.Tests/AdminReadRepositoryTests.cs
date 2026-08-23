using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Domain.Entities;
using Kanstraction.OnSite.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kanstraction.OnSite.Infrastructure.Tests;

public sealed class AdminReadRepositoryTests
{
    [Fact]
    public async Task Building_code_is_unique_within_a_project()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OnSiteDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new OnSiteDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var project = new Project { Name = "Project" };
        project.MutableBuildings.Add(new Building { Project = project, Code = "A" });
        project.MutableBuildings.Add(new Building { Project = project, Code = "A" });
        db.Add(project);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task StoppedBuilding_IsReportedAsStoppedWithCanonicalProgress()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OnSiteDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new OnSiteDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var project = new Project { Name = "Project" };
        var building = new Building
        {
            Project = project,
            Code = "A",
            Status = WorkStatus.Stopped
        };
        var stage = new Stage
        {
            Building = building,
            Name = "Stage",
            OrderIndex = 1,
            Status = WorkStatus.Stopped
        };
        stage.MutableSubStages.Add(new SubStage
        {
            Stage = stage,
            Name = "Sub",
            OrderIndex = 1,
            LaborCost = 1,
            Status = WorkStatus.Stopped
        });
        building.MutableStages.Add(stage);
        project.MutableBuildings.Add(building);
        db.Add(project);
        await db.SaveChangesAsync();

        var rows = await new EfAdminReadRepository(db)
            .ListBuildingsForOperationsAsync(project.Id);

        var row = Assert.Single(rows);
        Assert.Equal(WorkStatusDto.Stopped, row.Status);
        Assert.Equal(100, row.ProgressPercent);
    }
}
