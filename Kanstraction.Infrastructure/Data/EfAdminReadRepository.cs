using Kanstraction.Application.Abstractions;
using Kanstraction.Application.BuildingTypes.Queries.ListBuildingTypesForAdmin;
using Kanstraction.Application.Buildings.Queries.ListBuildingsForOperations;
using Kanstraction.Application.Materials.Queries.ListMaterialCategoriesForAdmin;
using Kanstraction.Application.Materials.Queries.ListMaterialsForAdmin;
using Kanstraction.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Infrastructure.Data;

public sealed class EfAdminReadRepository(AppDbContext db) : IAdminReadRepository
{
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
                var stageCount = allStages.Count;
                var completedCount = allStages.Count(s => s.Status == WorkStatus.Paid || s.Status == WorkStatus.Stopped);
                var progress = stageCount == 0 ? 0 : (int)Math.Round((completedCount / (double)stageCount) * 100d);

                var status = currentStage?.Status ?? (allStages.Count == 0 ? WorkStatus.NotStarted : WorkStatus.Paid);

                return new BuildingOperationsListItemDto(
                    b.Id,
                    b.Code,
                    b.BuildingType?.Name ?? string.Empty,
                    status,
                    progress,
                    currentStage?.Name ?? string.Empty,
                    hasPaidItems);
            })
            .ToList();
    }
}
