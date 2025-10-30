using Kanstraction.Application.Operations;
using Kanstraction.Domain.Entities;
using Kanstraction.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Infrastructure.Operations;

internal sealed class ProjectCatalogService : IProjectCatalogService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public ProjectCatalogService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IReadOnlyList<ProjectSummaryDto>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProjectSummaryDto(p.Id, p.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectDetailsDto?> GetProjectAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var project = await dbContext.Projects
            .AsNoTracking()
            .Include(p => p.Buildings)
                .ThenInclude(b => b.BuildingType)
            .Include(p => p.Buildings)
                .ThenInclude(b => b.Stages)
                    .ThenInclude(s => s.SubStages)
                        .ThenInclude(ss => ss.MaterialUsages)
                            .ThenInclude(mu => mu.Material)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return null;
        }

        var buildings = project.Buildings
            .OrderBy(b => b.Code)
            .Select(MapBuilding)
            .ToList();

        return new ProjectDetailsDto(project.Id, project.Name, buildings);
    }

    private static BuildingDto MapBuilding(Building building)
    {
        var stages = building.Stages
            .OrderBy(s => s.OrderIndex)
            .Select(MapStage)
            .ToList();

        var progress = ComputeProgress(stages.Select(s => s.Progress));

        return new BuildingDto(
            building.Id,
            building.Code,
            building.BuildingType?.Name ?? string.Empty,
            building.Status,
            progress,
            stages);
    }

    private static StageDto MapStage(Stage stage)
    {
        var subStages = stage.SubStages
            .OrderBy(ss => ss.OrderIndex)
            .Select(MapSubStage)
            .ToList();

        var progress = subStages.Count == 0
            ? MapStatus(stage.Status)
            : ComputeProgress(subStages.Select(ss => MapStatus(ss.Status)));

        return new StageDto(
            stage.Id,
            stage.Name,
            stage.Status,
            stage.OrderIndex,
            progress,
            subStages);
    }

    private static SubStageDto MapSubStage(SubStage subStage)
    {
        var materials = subStage.MaterialUsages
            .OrderBy(mu => mu.Material.Name)
            .Select(MapMaterialUsage)
            .ToList();

        return new SubStageDto(
            subStage.Id,
            subStage.Name,
            subStage.Status,
            subStage.OrderIndex,
            subStage.LaborCost,
            subStage.StartDate,
            subStage.EndDate,
            materials);
    }

    private static MaterialUsageDto MapMaterialUsage(MaterialUsage usage)
    {
        var unitPrice = usage.Material.PricePerUnit;
        var total = unitPrice * usage.Qty;
        return new MaterialUsageDto(
            usage.Id,
            usage.Material.Name,
            usage.Material.Unit,
            usage.Qty,
            unitPrice,
            total,
            usage.UsageDate,
            usage.Notes);
    }

    private static double MapStatus(WorkStatus status) => status switch
    {
        WorkStatus.NotStarted => 0d,
        WorkStatus.Ongoing => 0.5d,
        WorkStatus.Finished => 1d,
        WorkStatus.Paid => 1d,
        WorkStatus.Stopped => 0d,
        _ => 0d
    };

    private static double ComputeProgress(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count == 0)
        {
            return 0d;
        }

        return Math.Round(list.Average(), 2, MidpointRounding.AwayFromZero);
    }
}
