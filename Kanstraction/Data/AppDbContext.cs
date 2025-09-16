using Kanstraction.Entities;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace Kanstraction.Data;

public class AppDbContext : DbContext
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<BuildingType> BuildingTypes => Set<BuildingType>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Stage> Stages => Set<Stage>();
    public DbSet<SubStage> SubStages => Set<SubStage>();
    public DbSet<StagePreset> StagePresets => Set<StagePreset>();
    public DbSet<SubStagePreset> SubStagePresets => Set<SubStagePreset>();
    public DbSet<BuildingTypeStagePreset> BuildingTypeStagePresets => Set<BuildingTypeStagePreset>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<MaterialPriceHistory> MaterialPriceHistory => Set<MaterialPriceHistory>();
    public DbSet<MaterialUsagePreset> MaterialUsagesPreset => Set<MaterialUsagePreset>();
    public DbSet<MaterialUsage> MaterialUsages => Set<MaterialUsage>();

    public string DbPath { get; }

    public static string GetDefaultDbPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "Kanstraction");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, "app.db");
    }

    public AppDbContext()
    {
        DbPath = GetDefaultDbPath();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<BuildingTypeStagePreset>().HasKey(x => new { x.BuildingTypeId, x.StagePresetId });

        b.Entity<Building>()
            .HasOne(x => x.Project).WithMany(p => p.Buildings)
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Stage>()
            .HasOne(x => x.Building).WithMany(bd => bd.Stages)
            .HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<SubStage>()
            .HasOne(x => x.Stage).WithMany(st => st.SubStages)
            .HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<MaterialPriceHistory>()
            .HasOne(x => x.Material).WithMany(m => m.PriceHistory)
            .HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<MaterialUsage>()
            .HasOne(x => x.SubStage).WithMany(ss => ss.MaterialUsages)
            .HasForeignKey(x => x.SubStageId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MaterialUsage>()
            .HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId);

        // indexes
        b.Entity<Stage>().HasIndex(x => new { x.BuildingId, x.OrderIndex });
        b.Entity<SubStage>().HasIndex(x => new { x.StageId, x.OrderIndex });
        b.Entity<MaterialUsage>().HasIndex(x => new { x.SubStageId, x.UsageDate });
    }
}
