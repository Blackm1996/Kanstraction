using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.StagePresets.Commands.SaveStagePreset;
using Kanstraction.OnSite.Application.StagePresets.Queries.GetStagePresetEditor;
using Kanstraction.OnSite.Application.StagePresets.Queries.ListStagePresets;
using Kanstraction.OnSite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.OnSite.Infrastructure.Data;

public sealed class EfStagePresetRepository(OnSiteDbContext db) : IStagePresetRepository
{
    public async Task<IReadOnlyList<StagePresetListItemDto>> ListAsync(
        CancellationToken cancellationToken = default)
        => await db.StagePresets
            .AsNoTracking()
            .OrderBy(preset => preset.Name)
            .Select(preset => new StagePresetListItemDto(
                preset.Id,
                preset.Name,
                preset.IsActive,
                preset.SubStages.Count))
            .ToListAsync(cancellationToken);

    public async Task<StagePresetEditorDto?> GetEditorAsync(
        int stagePresetId,
        CancellationToken cancellationToken = default)
    {
        var preset = await db.StagePresets
            .AsNoTracking()
            .Where(item => item.Id == stagePresetId)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.IsActive,
                SubStages = item.SubStages
                    .OrderBy(subStage => subStage.OrderIndex)
                    .Select(subStage => new
                    {
                        subStage.Id,
                        subStage.Name,
                        subStage.OrderIndex
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (preset == null)
            return null;

        var subStageIds = preset.SubStages.Select(item => item.Id).ToList();
        var usages = await db.MaterialUsagesPreset
            .AsNoTracking()
            .Where(usage => subStageIds.Contains(usage.SubStagePresetId))
            .OrderBy(usage => usage.Material.MaterialCategory.Name)
            .ThenBy(usage => usage.Material.Name)
            .Select(usage => new
            {
                usage.Id,
                usage.SubStagePresetId,
                usage.MaterialId,
                MaterialName = usage.Material.Name,
                CategoryName = usage.Material.MaterialCategory.Name,
                usage.Material.Unit
            })
            .ToListAsync(cancellationToken);

        return new StagePresetEditorDto(
            preset.Id,
            preset.Name,
            preset.IsActive,
            preset.SubStages
                .Select(subStage => new StagePresetSubStageDto(
                    subStage.Id,
                    subStage.Name,
                    subStage.OrderIndex,
                    usages
                        .Where(usage => usage.SubStagePresetId == subStage.Id)
                        .Select(usage => new StagePresetMaterialDto(
                            usage.Id,
                            usage.MaterialId,
                            usage.MaterialName,
                            usage.CategoryName,
                            usage.Unit))
                        .ToList()))
                .ToList());
    }

    public Task<bool> NameExistsAsync(
        string name,
        int? excludingStagePresetId,
        CancellationToken cancellationToken = default)
        => db.StagePresets.AnyAsync(
            preset =>
                EF.Functions.Collate(preset.Name, "NOCASE") == name &&
                (!excludingStagePresetId.HasValue || preset.Id != excludingStagePresetId.Value),
            cancellationToken);

    public async Task<int> SaveAsync(
        StagePresetDefinition definition,
        CancellationToken cancellationToken = default)
    {
        StagePreset preset;
        if (definition.Id.HasValue)
        {
            preset = await db.StagePresets
                .FirstOrDefaultAsync(
                    item => item.Id == definition.Id.Value,
                    cancellationToken)
                ?? throw new InvalidOperationException("The stage preset was not found.");
            preset.Update(definition.Name, definition.IsActive);
        }
        else
        {
            preset = StagePreset.Create(definition.Name, definition.IsActive);
            db.StagePresets.Add(preset);
            await db.SaveChangesAsync(cancellationToken);
        }

        var existingSubStages = await db.SubStagePresets
            .Where(item => item.StagePresetId == preset.Id)
            .ToListAsync(cancellationToken);
        var submittedIds = definition.SubStages
            .Where(item => item.Id.HasValue)
            .Select(item => item.Id!.Value)
            .ToHashSet();
        var removedSubStages = existingSubStages
            .Where(item => !submittedIds.Contains(item.Id))
            .ToList();

        if (removedSubStages.Count > 0)
        {
            var removedIds = removedSubStages.Select(item => item.Id).ToList();
            var removedUsages = await db.MaterialUsagesPreset
                .Where(item => removedIds.Contains(item.SubStagePresetId))
                .ToListAsync(cancellationToken);
            db.MaterialUsagesPreset.RemoveRange(removedUsages);
            db.SubStagePresets.RemoveRange(removedSubStages);
        }

        var resolvedDefinitions = new List<(StagePresetSubStageDefinition Definition, SubStagePreset Entity)>();
        foreach (var subStageDefinition in definition.SubStages)
        {
            SubStagePreset entity;
            if (subStageDefinition.Id.HasValue)
            {
                entity = existingSubStages.FirstOrDefault(
                    item => item.Id == subStageDefinition.Id.Value)
                    ?? throw new InvalidOperationException(
                        "A submitted sub-stage does not belong to this preset.");
                entity.Update(subStageDefinition.Name, subStageDefinition.OrderIndex);
            }
            else
            {
                entity = SubStagePreset.Create(
                    preset.Id,
                    subStageDefinition.Name,
                    subStageDefinition.OrderIndex);
                db.SubStagePresets.Add(entity);
            }

            resolvedDefinitions.Add((subStageDefinition, entity));
        }

        await db.SaveChangesAsync(cancellationToken);

        var resolvedSubStageIds = resolvedDefinitions.Select(item => item.Entity.Id).ToList();
        var existingUsages = await db.MaterialUsagesPreset
            .Where(item => resolvedSubStageIds.Contains(item.SubStagePresetId))
            .ToListAsync(cancellationToken);

        foreach (var (subStageDefinition, subStage) in resolvedDefinitions)
        {
            var submittedMaterials = subStageDefinition.Materials
                .Select(item => item.MaterialId)
                .ToHashSet();
            var currentUsages = existingUsages
                .Where(item => item.SubStagePresetId == subStage.Id)
                .ToList();
            db.MaterialUsagesPreset.RemoveRange(
                currentUsages.Where(item => !submittedMaterials.Contains(item.MaterialId)));

            var existingMaterialIds = currentUsages
                .Select(item => item.MaterialId)
                .ToHashSet();
            foreach (var materialId in submittedMaterials.Where(id => !existingMaterialIds.Contains(id)))
            {
                db.MaterialUsagesPreset.Add(
                    MaterialUsagePreset.Create(subStage.Id, materialId));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return preset.Id;
    }
}
