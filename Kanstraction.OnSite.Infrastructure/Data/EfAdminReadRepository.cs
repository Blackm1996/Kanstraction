using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.BuildingTypes.Queries.ListBuildingTypesForAdmin;
using Kanstraction.OnSite.Application.BuildingTypes.Queries.ListBuildingTypesForCreation;
using Kanstraction.OnSite.Application.Buildings.Queries.ListBuildingsForOperations;
using Kanstraction.OnSite.Application.Materials.Queries.ListMaterialCategoriesForAdmin;
using Kanstraction.OnSite.Application.Materials.Queries.ListMaterialsForAdmin;
using Kanstraction.OnSite.Application.Materials.Queries.GetMaterialDetails;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Domain.Entities;
using Kanstraction.OnSite.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.OnSite.Infrastructure.Data;

public sealed class EfAdminReadRepository(OnSiteDbContext db) : IAdminReadRepository
{
    public async Task<MaterialDetailsDto?> GetMaterialDetailsAsync(
        int materialId,
        CancellationToken cancellationToken = default)
    {
        var material = await db.Materials
            .AsNoTracking()
            .Where(item => item.Id == materialId)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Unit,
                item.PricePerUnit,
                item.IsActive,
                item.MaterialCategoryId,
                CategoryName = item.MaterialCategory.Name
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (material == null)
            return null;

        var history = await db.MaterialPriceHistory
            .AsNoTracking()
            .Where(item => item.MaterialId == materialId)
            .OrderByDescending(item => item.StartDate)
            .Select(item => new MaterialPriceHistoryDto(
                item.StartDate,
                item.EndDate,
                item.PricePerUnit))
            .ToListAsync(cancellationToken);

        return new MaterialDetailsDto(
            material.Id,
            material.Name,
            material.Unit,
            material.PricePerUnit,
            material.IsActive,
            material.MaterialCategoryId,
            material.CategoryName,
            history);
    }

    public async Task<IReadOnlyList<MaterialCategoryListItemDto>> ListMaterialCategoriesAsync(CancellationToken cancellationToken = default)
        => await db.MaterialCategories
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new MaterialCategoryListItemDto(x.Id, x.Name))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MaterialListItemDto>> ListMaterialsAsync(CancellationToken cancellationToken = default)
        => await db.Materials
            .AsNoTracking()
            .Include(x => x.MaterialCategory)
            .OrderBy(x => x.Name)
            .Select(x => new MaterialListItemDto(
                x.Id,
                x.Name,
                x.Unit,
                x.IsActive,
                x.PricePerUnit,
                x.MaterialCategoryId,
                x.MaterialCategory != null ? x.MaterialCategory.Name : string.Empty))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BuildingTypeListItemDto>> ListBuildingTypesAsync(CancellationToken cancellationToken = default)
        => await db.BuildingTypes
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new BuildingTypeListItemDto(x.Id, x.Name, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BuildingTypeCreationOptionDto>> ListBuildingTypesForCreationAsync(
        CancellationToken cancellationToken = default)
    {
        var types = await db.BuildingTypes
            .AsNoTracking()
            .Where(type => type.IsActive)
            .OrderBy(type => type.Name)
            .ToListAsync(cancellationToken);

        var typeIds = types.Select(type => type.Id).ToList();
        var stageLinks = await db.BuildingTypeStagePresets
            .AsNoTracking()
            .Where(link => typeIds.Contains(link.BuildingTypeId))
            .ToListAsync(cancellationToken);

        var stagePresetIds = stageLinks.Select(link => link.StagePresetId).Distinct().ToList();
        var subStages = await db.SubStagePresets
            .AsNoTracking()
            .Where(subStage => stagePresetIds.Contains(subStage.StagePresetId))
            .ToListAsync(cancellationToken);

        var subStageIds = subStages.Select(subStage => subStage.Id).ToList();
        var laborValues = await db.BuildingTypeSubStageLabors
            .AsNoTracking()
            .Where(value =>
                typeIds.Contains(value.BuildingTypeId) &&
                subStageIds.Contains(value.SubStagePresetId))
            .ToListAsync(cancellationToken);
        var requiredMaterials = await db.MaterialUsagesPreset
            .AsNoTracking()
            .Where(value => subStageIds.Contains(value.SubStagePresetId))
            .ToListAsync(cancellationToken);
        var materialValues = await db.BuildingTypeMaterialUsages
            .AsNoTracking()
            .Where(value =>
                typeIds.Contains(value.BuildingTypeId) &&
                subStageIds.Contains(value.SubStagePresetId))
            .ToListAsync(cancellationToken);

        return types
            .Select(type =>
            {
                var assignedStageIds = stageLinks
                    .Where(link => link.BuildingTypeId == type.Id)
                    .Select(link => link.StagePresetId)
                    .ToHashSet();
                var requiredSubStageIds = subStages
                    .Where(subStage => assignedStageIds.Contains(subStage.StagePresetId))
                    .Select(subStage => subStage.Id)
                    .ToHashSet();

                var hasMissingLabor = requiredSubStageIds.Any(subStageId =>
                    !laborValues.Any(value =>
                        value.BuildingTypeId == type.Id &&
                        value.SubStagePresetId == subStageId &&
                        value.LaborCost.HasValue));

                var hasMissingMaterial = requiredMaterials
                    .Where(required => requiredSubStageIds.Contains(required.SubStagePresetId))
                    .Any(required => !materialValues.Any(value =>
                        value.BuildingTypeId == type.Id &&
                        value.SubStagePresetId == required.SubStagePresetId &&
                        value.MaterialId == required.MaterialId &&
                        value.Qty.HasValue));

                return new BuildingTypeCreationOptionDto(
                    type.Id,
                    type.Name,
                    assignedStageIds.Count > 0,
                    hasMissingLabor || hasMissingMaterial);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<BuildingOperationsListItemDto>> ListBuildingsForOperationsAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var buildings = await db.Buildings
            .AsNoTracking()
            .Include(b => b.BuildingType)
            .Include(b => b.Stages)
                .ThenInclude(s => s.SubStages)
                    .ThenInclude(ss => ss.MaterialUsages)
            .Where(b => b.ProjectId == projectId)
            .OrderBy(b => b.Code)
            .ToListAsync(cancellationToken);

        return buildings
            .Select(b =>
            {
                var allStages = b.Stages?.OrderBy(s => s.OrderIndex).ToList() ?? new List<Stage>();
                var currentStage = allStages.FirstOrDefault(s => s.Status != WorkStatus.Paid && s.Status != WorkStatus.Stopped);
                var hasPaidItems = allStages.Any(s => s.Status == WorkStatus.Paid || (s.SubStages?.Any(ss => ss.Status == WorkStatus.Paid) ?? false));
                var progress = (int)Math.Round(
                    WorkProgressRules.ComputeBuildingProgress(b) * 100d,
                    MidpointRounding.AwayFromZero);

                return new BuildingOperationsListItemDto(
                    b.Id,
                    b.Code,
                    b.BuildingType?.Name ?? string.Empty,
                    (WorkStatusDto)(int)b.Status,
                    progress,
                    currentStage?.Name ?? string.Empty,
                    hasPaidItems);
            })
            .ToList();
    }
}
