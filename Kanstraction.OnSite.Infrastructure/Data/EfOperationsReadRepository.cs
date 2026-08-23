using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Materials.Queries.ListMaterialUsages;
using Kanstraction.OnSite.Application.Materials.Queries.ListAvailableMaterials;
using Kanstraction.OnSite.Application.Stages.Queries.GetStageAdditionOptions;
using Kanstraction.OnSite.Application.Operations.Queries.GetBuildingWorkspace;
using Kanstraction.OnSite.Application.Reports.Queries.GetOperationsReportData;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Domain.Entities;
using Kanstraction.OnSite.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.OnSite.Infrastructure.Data;

public sealed class EfOperationsReadRepository(OnSiteDbContext db) : IOperationsReadRepository
{
    public async Task<ProjectOperationsReportDataDto?> GetProjectReportDataAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(item => item.Id == projectId)
            .Select(item => new { item.Id, item.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (project == null)
            return null;

        var buildings = await db.Buildings
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .Include(item => item.BuildingType)
            .Include(item => item.Stages)
                .ThenInclude(stage => stage.SubStages)
                    .ThenInclude(subStage => subStage.MaterialUsages)
                        .ThenInclude(usage => usage.Material)
                            .ThenInclude(material => material.MaterialCategory)
            .Include(item => item.Stages)
                .ThenInclude(stage => stage.SubStages)
                    .ThenInclude(subStage => subStage.MaterialUsages)
                        .ThenInclude(usage => usage.Material)
                            .ThenInclude(material => material.PriceHistory)
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken);

        var buildingTypeIds = buildings
            .Where(item => item.BuildingTypeId.HasValue)
            .Select(item => item.BuildingTypeId!.Value)
            .Distinct()
            .ToList();
        var buildingTypes = await db.BuildingTypes
            .AsNoTracking()
            .Where(item => buildingTypeIds.Contains(item.Id))
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);
        var assignments = await db.BuildingTypeStagePresets
            .AsNoTracking()
            .Where(item => buildingTypeIds.Contains(item.BuildingTypeId))
            .Include(item => item.StagePreset)
            .OrderBy(item => item.OrderIndex)
            .ToListAsync(cancellationToken);
        var stagePresetIds = assignments
            .Select(item => item.StagePresetId)
            .Distinct()
            .ToList();
        var subStagePresets = await db.SubStagePresets
            .AsNoTracking()
            .Where(item => stagePresetIds.Contains(item.StagePresetId))
            .OrderBy(item => item.OrderIndex)
            .ToListAsync(cancellationToken);
        var inclusionValues = await db.BuildingTypeSubStageLabors
            .AsNoTracking()
            .Where(item => buildingTypeIds.Contains(item.BuildingTypeId))
            .ToListAsync(cancellationToken);

        var typeDtos = buildingTypes.Select(type =>
        {
            var typeAssignments = assignments
                .Where(item => item.BuildingTypeId == type.Id)
                .OrderBy(item => item.OrderIndex)
                .ToList();
            return new ReportBuildingTypeDto(
                type.Id,
                type.Name,
                typeAssignments.Select(assignment =>
                {
                    var subStages = subStagePresets
                        .Where(item => item.StagePresetId == assignment.StagePresetId)
                        .OrderBy(item => item.OrderIndex)
                        .ToList();
                    return new ReportStageTemplateDto(
                        assignment.StagePresetId,
                        assignment.StagePreset?.Name ?? string.Empty,
                        assignment.OrderIndex,
                        subStages.Select(subStage => new ReportSubStageTemplateDto(
                            subStage.Id,
                            subStage.Name,
                            subStage.OrderIndex,
                            inclusionValues.FirstOrDefault(value =>
                                value.BuildingTypeId == type.Id &&
                                value.SubStagePresetId == subStage.Id)?
                                .IncludeInReport))
                        .ToList());
                }).ToList());
        }).ToList();

        return new ProjectOperationsReportDataDto(
            project.Id,
            project.Name,
            typeDtos,
            buildings.Select(MapBuilding).ToList());
    }

    public async Task<ReportStageDto?> GetStageReportDataAsync(
        int stageId,
        CancellationToken cancellationToken = default)
    {
        var stage = await StageReportQuery()
            .FirstOrDefaultAsync(item => item.Id == stageId, cancellationToken);
        return stage == null ? null : MapStage(stage);
    }

    public async Task<ReportSubStageDto?> GetSubStageReportDataAsync(
        int subStageId,
        CancellationToken cancellationToken = default)
    {
        var subStage = await db.SubStages
            .AsNoTracking()
            .Include(item => item.MaterialUsages)
                .ThenInclude(usage => usage.Material)
                    .ThenInclude(material => material.MaterialCategory)
            .Include(item => item.MaterialUsages)
                .ThenInclude(usage => usage.Material)
                    .ThenInclude(material => material.PriceHistory)
            .FirstOrDefaultAsync(item => item.Id == subStageId, cancellationToken);
        return subStage == null ? null : MapSubStage(subStage);
    }

    public async Task<BuildingWorkspaceDto?> GetBuildingWorkspaceAsync(
        int buildingId,
        CancellationToken cancellationToken = default)
    {
        var building = await db.Buildings
            .AsNoTracking()
            .Include(item => item.Stages)
                .ThenInclude(stage => stage.SubStages)
            .FirstOrDefaultAsync(item => item.Id == buildingId, cancellationToken);
        if (building == null)
            return null;

        return new BuildingWorkspaceDto(
            building.Id,
            building.Stages
                .OrderBy(stage => stage.OrderIndex)
                .Select(stage => new OperationsStageDto(
                    stage.Id,
                    stage.Name,
                    stage.OrderIndex,
                    (WorkStatusDto)(int)stage.Status,
                    WorkProgressRules.ComputeStageProgress(stage) * 100d,
                    stage.Status is WorkStatus.Finished or WorkStatus.Paid or WorkStatus.Stopped
                        ? string.Empty
                        : stage.SubStages
                            .OrderBy(subStage => subStage.OrderIndex)
                            .FirstOrDefault(subStage => subStage.Status == WorkStatus.Ongoing)?
                            .Name ?? string.Empty,
                    stage.Status == WorkStatus.Paid ? stage.EndDate : null,
                    stage.SubStages
                        .OrderBy(subStage => subStage.OrderIndex)
                        .Select(subStage => new OperationsSubStageDto(
                            subStage.Id,
                            subStage.StageId,
                            subStage.Name,
                            subStage.OrderIndex,
                            (WorkStatusDto)(int)subStage.Status,
                            subStage.StartDate,
                            subStage.EndDate,
                            subStage.LaborCost))
                        .ToList()))
                .ToList());
    }

    public async Task<StageAdditionOptionsDto?> GetStageAdditionOptionsAsync(
        int buildingId,
        CancellationToken cancellationToken = default)
    {
        var building = await db.Buildings
            .AsNoTracking()
            .Where(item => item.Id == buildingId)
            .Select(item => new
            {
                item.BuildingTypeId,
                StageNames = item.Stages.Select(stage => stage.Name).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (building?.BuildingTypeId == null)
            return null;

        var presets = await db.StagePresets
            .AsNoTracking()
            .Where(preset => preset.IsActive && !building.StageNames.Contains(preset.Name))
            .OrderBy(preset => preset.Name)
            .Select(preset => new StagePresetOptionDto(preset.Id, preset.Name))
            .ToListAsync(cancellationToken);

        return new StageAdditionOptionsDto(
            building.BuildingTypeId.Value,
            building.StageNames.Count,
            presets);
    }

    public async Task<IReadOnlyList<AvailableMaterialDto>> ListAvailableMaterialsAsync(
        int subStageId,
        CancellationToken cancellationToken = default)
    {
        var usedIds = db.MaterialUsages
            .Where(usage => usage.SubStageId == subStageId)
            .Select(usage => usage.MaterialId);

        return await db.Materials
            .AsNoTracking()
            .Where(material => material.IsActive && !usedIds.Contains(material.Id))
            .OrderBy(material => material.MaterialCategory.Name)
            .ThenBy(material => material.Name)
            .Select(material => new AvailableMaterialDto(
                material.Id,
                material.Name,
                material.Unit,
                material.MaterialCategory.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MaterialUsageListItemDto>> ListMaterialUsagesAsync(
        int subStageId,
        bool freezePrices,
        CancellationToken cancellationToken = default)
    {
        var usages = await db.MaterialUsages
            .AsNoTracking()
            .Include(usage => usage.Material)
                .ThenInclude(material => material.PriceHistory)
            .Include(usage => usage.Material)
                .ThenInclude(material => material.MaterialCategory)
            .Where(usage => usage.SubStageId == subStageId)
            .OrderBy(usage => usage.Material.Name)
            .ToListAsync(cancellationToken);

        return usages
            .Select(usage => new MaterialUsageListItemDto(
                usage.Id,
                usage.SubStageId,
                usage.Material.Name,
                usage.Material.MaterialCategory?.Name ?? string.Empty,
                usage.Material.Unit,
                usage.Qty,
                usage.UsageDate,
                ResolveUnitPrice(usage, freezePrices)))
            .ToList();
    }

    private static decimal ResolveUnitPrice(Domain.Entities.MaterialUsage usage, bool freezePrices)
    {
        if (!freezePrices || usage.UsageDate == default)
            return usage.Material.PricePerUnit;

        var usageDate = usage.UsageDate.Date;
        return usage.Material.PriceHistory
            .Where(price =>
                price.StartDate.Date <= usageDate &&
                (!price.EndDate.HasValue || price.EndDate.Value.Date >= usageDate))
            .OrderByDescending(price => price.StartDate)
            .Select(price => (decimal?)price.PricePerUnit)
            .FirstOrDefault() ?? usage.Material.PricePerUnit;
    }

    private IQueryable<Stage> StageReportQuery()
        => db.Stages
            .AsNoTracking()
            .Include(item => item.SubStages)
                .ThenInclude(subStage => subStage.MaterialUsages)
                    .ThenInclude(usage => usage.Material)
                        .ThenInclude(material => material.MaterialCategory)
            .Include(item => item.SubStages)
                .ThenInclude(subStage => subStage.MaterialUsages)
                    .ThenInclude(usage => usage.Material)
                        .ThenInclude(material => material.PriceHistory);

    private static ReportBuildingDto MapBuilding(Building building)
        => new(
            building.Id,
            building.Code,
            building.BuildingTypeId,
            building.BuildingType?.Name ?? string.Empty,
            (WorkStatusDto)(int)building.Status,
            building.Stages
                .OrderBy(stage => stage.OrderIndex)
                .Select(MapStage)
                .ToList());

    private static ReportStageDto MapStage(Stage stage)
        => new(
            stage.Id,
            stage.Name,
            stage.OrderIndex,
            (WorkStatusDto)(int)stage.Status,
            stage.StartDate,
            stage.EndDate,
            stage.SubStages
                .OrderBy(subStage => subStage.OrderIndex)
                .Select(MapSubStage)
                .ToList());

    private static ReportSubStageDto MapSubStage(SubStage subStage)
        => new(
            subStage.Id,
            subStage.Name,
            subStage.OrderIndex,
            (WorkStatusDto)(int)subStage.Status,
            subStage.StartDate,
            subStage.EndDate,
            subStage.LaborCost,
            subStage.MaterialUsages
                .OrderBy(usage => usage.Material.Name)
                .Select(usage => new ReportMaterialUsageDto(
                    usage.Id,
                    usage.Qty,
                    usage.UsageDate,
                    new ReportMaterialDto(
                        usage.Material.Id,
                        usage.Material.Name,
                        usage.Material.MaterialCategory?.Name ?? string.Empty,
                        usage.Material.Unit,
                        usage.Material.PricePerUnit,
                        usage.Material.PriceHistory
                            .OrderBy(price => price.StartDate)
                            .Select(price => new ReportPriceDto(
                                price.StartDate,
                                price.EndDate,
                                price.PricePerUnit))
                            .ToList())))
                .ToList());
}
