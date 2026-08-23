using Kanstraction.OnSite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.OnSite.Infrastructure.Data;

public class OnSiteDbContext : DbContext
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

    public OnSiteDbContext(DbContextOptions<OnSiteDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Project>()
            .Navigation(project => project.Buildings)
            .HasField("_buildings")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Entity<Building>()
            .Navigation(building => building.Stages)
            .HasField("_stages")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Entity<Stage>()
            .Navigation(stage => stage.SubStages)
            .HasField("_subStages")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Entity<SubStage>()
            .Navigation(subStage => subStage.MaterialUsages)
            .HasField("_materialUsages")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Entity<Material>()
            .Navigation(material => material.PriceHistory)
            .HasField("_priceHistory")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Entity<MaterialCategory>()
            .Navigation(category => category.Materials)
            .HasField("_materials")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Entity<StagePreset>()
            .Navigation(preset => preset.SubStages)
            .HasField("_subStages")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

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
        b.Entity<Building>().HasIndex(x => new { x.ProjectId, x.Code }).IsUnique();
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
