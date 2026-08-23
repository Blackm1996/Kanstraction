using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Buildings;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.OnSite.Infrastructure.Data;

public sealed class EfBuildingTemplateReader(OnSiteDbContext db) : IBuildingTemplateReader
{
    public async Task<BuildingTemplateDefinition?> GetAsync(
        int buildingTypeId,
        CancellationToken cancellationToken = default)
    {
        if (!await db.BuildingTypes.AnyAsync(
                type => type.Id == buildingTypeId,
                cancellationToken))
        {
            return null;
        }

        var stageLinks = await db.BuildingTypeStagePresets
            .AsNoTracking()
            .Where(link => link.BuildingTypeId == buildingTypeId)
            .Include(link => link.StagePreset)
            .OrderBy(link => link.OrderIndex)
            .ToListAsync(cancellationToken);

        var stagePresetIds = stageLinks.Select(link => link.StagePresetId).ToList();
        var subStagePresets = await db.SubStagePresets
            .AsNoTracking()
            .Where(subStage => stagePresetIds.Contains(subStage.StagePresetId))
            .OrderBy(subStage => subStage.OrderIndex)
            .ToListAsync(cancellationToken);

        var subStagePresetIds = subStagePresets.Select(subStage => subStage.Id).ToList();
        var labor = await db.BuildingTypeSubStageLabors
            .AsNoTracking()
            .Where(value =>
                value.BuildingTypeId == buildingTypeId &&
                subStagePresetIds.Contains(value.SubStagePresetId))
            .ToDictionaryAsync(
                value => value.SubStagePresetId,
                value => value.LaborCost ?? 0m,
                cancellationToken);

        var requiredMaterials = await db.MaterialUsagesPreset
            .AsNoTracking()
            .Where(value => subStagePresetIds.Contains(value.SubStagePresetId))
            .ToListAsync(cancellationToken);

        var quantities = await db.BuildingTypeMaterialUsages
            .AsNoTracking()
            .Where(value =>
                value.BuildingTypeId == buildingTypeId &&
                subStagePresetIds.Contains(value.SubStagePresetId))
            .ToDictionaryAsync(
                value => (value.SubStagePresetId, value.MaterialId),
                value => value.Qty ?? 0m,
                cancellationToken);

        var materialsBySubStage = requiredMaterials
            .GroupBy(value => value.SubStagePresetId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<BuildingMaterialTemplateDefinition>)group
                    .Select(value => new BuildingMaterialTemplateDefinition(
                        value.MaterialId,
                        quantities.GetValueOrDefault((value.SubStagePresetId, value.MaterialId))))
                    .ToList());

        var subStagesByPreset = subStagePresets
            .GroupBy(subStage => subStage.StagePresetId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<BuildingSubStageTemplateDefinition>)group
                    .Select(subStage => new BuildingSubStageTemplateDefinition(
                        subStage.Name,
                        subStage.OrderIndex,
                        labor.GetValueOrDefault(subStage.Id),
                        materialsBySubStage.GetValueOrDefault(
                            subStage.Id,
                            Array.Empty<BuildingMaterialTemplateDefinition>())))
                    .ToList());

        var stages = stageLinks
            .Select(link => new BuildingStageTemplateDefinition(
                link.StagePresetId,
                link.StagePreset.Name,
                link.OrderIndex,
                subStagesByPreset.GetValueOrDefault(
                    link.StagePresetId,
                    Array.Empty<BuildingSubStageTemplateDefinition>())))
            .ToList();

        return new BuildingTemplateDefinition(buildingTypeId, stages);
    }
}
