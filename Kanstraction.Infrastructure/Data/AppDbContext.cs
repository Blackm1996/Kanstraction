using Kanstraction.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace Kanstraction.Infrastructure.Data;

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
    public DbSet<BuildingTypeSubStageLabor> BuildingTypeSubStageLabors => Set<BuildingTypeSubStageLabor>();
    public DbSet<BuildingTypeMaterialUsage> BuildingTypeMaterialUsages => Set<BuildingTypeMaterialUsage>();
    public DbSet<MaterialCategory> MaterialCategories => Set<MaterialCategory>();
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
        => options.UseSqlite(BuildConnectionString(DbPath));

    private static string BuildConnectionString(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        };

        return builder.ToString();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<BuildingTypeStagePreset>().HasKey(x => new { x.BuildingTypeId, x.StagePresetId });

        b.Entity<BuildingTypeSubStageLabor>().HasKey(x => new { x.BuildingTypeId, x.SubStagePresetId });
        b.Entity<BuildingTypeSubStageLabor>().HasIndex(x => x.SubStagePresetId);

        b.Entity<BuildingTypeSubStageLabor>()
            .HasOne(x => x.BuildingType)
            .WithMany()
            .HasForeignKey(x => x.BuildingTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<BuildingTypeSubStageLabor>()
            .HasOne(x => x.SubStagePreset)
            .WithMany()
            .HasForeignKey(x => x.SubStagePresetId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<BuildingTypeMaterialUsage>().HasKey(x => new { x.BuildingTypeId, x.SubStagePresetId, x.MaterialId });
        b.Entity<BuildingTypeMaterialUsage>().HasIndex(x => x.SubStagePresetId);
        b.Entity<BuildingTypeMaterialUsage>().HasIndex(x => x.MaterialId);

        b.Entity<BuildingTypeMaterialUsage>()
            .HasOne(x => x.BuildingType)
            .WithMany()
            .HasForeignKey(x => x.BuildingTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<BuildingTypeMaterialUsage>()
            .HasOne(x => x.SubStagePreset)
            .WithMany()
            .HasForeignKey(x => x.SubStagePresetId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<BuildingTypeMaterialUsage>()
            .HasOne(x => x.Material)
            .WithMany()
            .HasForeignKey(x => x.MaterialId)
            .OnDelete(DeleteBehavior.Cascade);

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

        b.Entity<Material>()
            .HasOne(m => m.MaterialCategory)
            .WithMany(c => c.Materials)
            .HasForeignKey(m => m.MaterialCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<MaterialUsage>()
            .HasOne(x => x.SubStage).WithMany(ss => ss.MaterialUsages)
            .HasForeignKey(x => x.SubStageId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MaterialUsage>()
            .HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId);

        // indexes
        b.Entity<MaterialCategory>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Material>().HasIndex(x => x.MaterialCategoryId);
        b.Entity<Material>().HasIndex(x => x.Name).IsUnique();
        b.Entity<MaterialUsagePreset>().HasIndex(x => x.SubStagePresetId);
        b.Entity<MaterialUsagePreset>().HasIndex(x => x.MaterialId);
        b.Entity<MaterialUsagePreset>().HasIndex(x => new { x.SubStagePresetId, x.MaterialId }).IsUnique();
        b.Entity<Stage>().HasIndex(x => new { x.BuildingId, x.OrderIndex });
        b.Entity<SubStage>().HasIndex(x => new { x.StageId, x.OrderIndex });
        b.Entity<MaterialUsage>().HasIndex(x => new { x.SubStageId, x.UsageDate });
    }
}
