using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.BuildingTypes.Commands.SaveBuildingType;
using Kanstraction.OnSite.Application.BuildingTypes.Queries.GetBuildingTypeEditor;
using Kanstraction.OnSite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.OnSite.Infrastructure.Data;

public sealed class EfBuildingTypeRepository(OnSiteDbContext db) : IBuildingTypeRepository
{
    public async Task<BuildingTypeEditorWorkspaceDto?> GetEditorWorkspaceAsync(
        int? buildingTypeId,
        CancellationToken cancellationToken = default)
    {
        var stageRows = await db.StagePresets
            .AsNoTracking()
            .OrderBy(stage => stage.Name)
            .Select(stage => new
            {
                stage.Id,
                stage.Name,
                stage.IsActive,
                SubStages = stage.SubStages
                    .OrderBy(subStage => subStage.OrderIndex)
                    .Select(subStage => new
                    {
                        subStage.Id,
                        subStage.Name,
                        subStage.OrderIndex
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var subStageIds = stageRows
            .SelectMany(stage => stage.SubStages)
            .Select(subStage => subStage.Id)
            .ToList();
        var materialTemplates = await db.MaterialUsagesPreset
            .AsNoTracking()
            .Where(item => subStageIds.Contains(item.SubStagePresetId))
            .OrderBy(item => item.Material.Name)
            .Select(item => new
            {
                item.SubStagePresetId,
                item.MaterialId,
                MaterialName = item.Material.Name,
                item.Material.Unit
            })
            .ToListAsync(cancellationToken);

        BuildingTypeEditorDto? editor = null;
        var laborValues = new Dictionary<int, BuildingTypeSubStageLabor>();
        var materialValues = new Dictionary<(int, int), decimal?>();
        var assignedRows = new List<BuildingTypeStagePreset>();
        BuildingType? buildingType = null;

        if (buildingTypeId.HasValue)
        {
            buildingType = await db.BuildingTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.Id == buildingTypeId.Value,
                    cancellationToken);
            if (buildingType == null)
                return null;

            assignedRows = await db.BuildingTypeStagePresets
                .AsNoTracking()
                .Where(item => item.BuildingTypeId == buildingTypeId.Value)
                .OrderBy(item => item.OrderIndex)
                .ToListAsync(cancellationToken);
            laborValues = await db.BuildingTypeSubStageLabors
                .AsNoTracking()
                .Where(item => item.BuildingTypeId == buildingTypeId.Value)
                .ToDictionaryAsync(
                    item => item.SubStagePresetId,
                    cancellationToken);
            materialValues = await db.BuildingTypeMaterialUsages
                .AsNoTracking()
                .Where(item => item.BuildingTypeId == buildingTypeId.Value)
                .ToDictionaryAsync(
                    item => new ValueTuple<int, int>(
                        item.SubStagePresetId,
                        item.MaterialId),
                    item => item.Qty,
                    cancellationToken);
        }

        BuildingTypeStageEditorDto BuildStage(
            int stagePresetId,
            int orderIndex,
            bool includeSavedValues)
        {
            var stage = stageRows.First(item => item.Id == stagePresetId);
            return new BuildingTypeStageEditorDto(
                stage.Id,
                stage.Name,
                orderIndex,
                stage.SubStages
                    .Select(subStage =>
                    {
                        laborValues.TryGetValue(subStage.Id, out var labor);
                        return new BuildingTypeSubStageEditorDto(
                            subStage.Id,
                            subStage.Name,
                            subStage.OrderIndex,
                            includeSavedValues ? labor?.LaborCost : null,
                            includeSavedValues && (labor?.IncludeInReport ?? false),
                            materialTemplates
                                .Where(material => material.SubStagePresetId == subStage.Id)
                                .Select(material => new BuildingTypeMaterialEditorDto(
                                    material.MaterialId,
                                    material.MaterialName,
                                    material.Unit,
                                    includeSavedValues &&
                                    materialValues.TryGetValue(
                                        (subStage.Id, material.MaterialId),
                                        out var quantity)
                                        ? quantity
                                        : null))
                                .ToList());
                    })
                    .ToList());
        }

        var templates = stageRows
            .Select(stage => BuildStage(stage.Id, 0, false))
            .ToList();

        if (buildingType != null)
        {
            editor = new BuildingTypeEditorDto(
                buildingType.Id,
                buildingType.Name,
                buildingType.IsActive,
                assignedRows
                    .Select(assignment => BuildStage(
                        assignment.StagePresetId,
                        assignment.OrderIndex,
                        true))
                    .ToList());
        }

        return new BuildingTypeEditorWorkspaceDto(editor, templates);
    }

    public Task<bool> NameExistsAsync(
        string name,
        int? excludingBuildingTypeId,
        CancellationToken cancellationToken = default)
        => db.BuildingTypes.AnyAsync(
            item =>
                EF.Functions.Collate(item.Name, "NOCASE") == name &&
                (!excludingBuildingTypeId.HasValue || item.Id != excludingBuildingTypeId.Value),
            cancellationToken);

    public async Task<int> SaveAsync(
        BuildingTypeDefinition definition,
        CancellationToken cancellationToken = default)
    {
        BuildingType buildingType;
        if (definition.Id.HasValue)
        {
            buildingType = await db.BuildingTypes.FirstOrDefaultAsync(
                item => item.Id == definition.Id.Value,
                cancellationToken)
                ?? throw new InvalidOperationException("The building type was not found.");
            buildingType.Update(definition.Name, definition.IsActive);
        }
        else
        {
            buildingType = BuildingType.Create(definition.Name, definition.IsActive);
            db.BuildingTypes.Add(buildingType);
            await db.SaveChangesAsync(cancellationToken);
        }

        var existingStageLinks = await db.BuildingTypeStagePresets
            .Where(item => item.BuildingTypeId == buildingType.Id)
            .ToListAsync(cancellationToken);
        db.BuildingTypeStagePresets.RemoveRange(existingStageLinks);
        foreach (var stage in definition.Stages)
        {
            db.BuildingTypeStagePresets.Add(
                BuildingTypeStagePreset.Create(
                    buildingType.Id,
                    stage.StagePresetId,
                    stage.OrderIndex));
        }

        var submittedSubStageIds = definition.Stages
            .SelectMany(stage => stage.SubStages)
            .Select(subStage => subStage.SubStagePresetId)
            .ToHashSet();
        var existingLabors = await db.BuildingTypeSubStageLabors
            .Where(item => item.BuildingTypeId == buildingType.Id)
            .ToListAsync(cancellationToken);
        db.BuildingTypeSubStageLabors.RemoveRange(
            existingLabors.Where(item => !submittedSubStageIds.Contains(item.SubStagePresetId)));

        var submittedMaterialKeys = definition.Stages
            .SelectMany(stage => stage.SubStages)
            .SelectMany(subStage => subStage.Materials.Select(material =>
                (subStage.SubStagePresetId, material.MaterialId)))
            .ToHashSet();
        var existingMaterials = await db.BuildingTypeMaterialUsages
            .Where(item => item.BuildingTypeId == buildingType.Id)
            .ToListAsync(cancellationToken);
        db.BuildingTypeMaterialUsages.RemoveRange(
            existingMaterials.Where(item =>
                !submittedMaterialKeys.Contains(
                    (item.SubStagePresetId, item.MaterialId))));

        foreach (var subStage in definition.Stages.SelectMany(stage => stage.SubStages))
        {
            var labor = existingLabors.FirstOrDefault(
                item => item.SubStagePresetId == subStage.SubStagePresetId);
            if (labor == null)
            {
                labor = BuildingTypeSubStageLabor.Create(
                    buildingType.Id,
                    subStage.SubStagePresetId,
                    subStage.LaborCost,
                    subStage.IncludeInReport);
                db.BuildingTypeSubStageLabors.Add(labor);
            }
            else
            {
                labor.Update(subStage.LaborCost, subStage.IncludeInReport);
            }

            foreach (var material in subStage.Materials)
            {
                var row = existingMaterials.FirstOrDefault(item =>
                    item.SubStagePresetId == subStage.SubStagePresetId &&
                    item.MaterialId == material.MaterialId);
                if (row == null)
                {
                    row = BuildingTypeMaterialUsage.Create(
                        buildingType.Id,
                        subStage.SubStagePresetId,
                        material.MaterialId,
                        material.Quantity);
                    db.BuildingTypeMaterialUsages.Add(row);
                }
                else
                {
                    row.UpdateQuantity(material.Quantity);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return buildingType.Id;
    }
}
