using Kanstraction.Application.BuildingTypes.Queries.ListBuildingTypesForAdmin;
using Kanstraction.Application.Buildings.Queries.ListBuildingsForOperations;
using Kanstraction.Application.Materials.Queries.ListMaterialCategoriesForAdmin;
using Kanstraction.Application.Materials.Queries.ListMaterialsForAdmin;

namespace Kanstraction.Application.Abstractions;

public interface IAdminReadRepository
{
    Task<IReadOnlyList<MaterialCategoryListItemDto>> ListMaterialCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaterialListItemDto>> ListMaterialsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BuildingTypeListItemDto>> ListBuildingTypesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BuildingOperationsListItemDto>> ListBuildingsForOperationsAsync(int projectId, CancellationToken cancellationToken = default);
}
