using Kanstraction.OnSite.Application.BuildingTypes.Queries.ListBuildingTypesForAdmin;
using Kanstraction.OnSite.Application.BuildingTypes.Queries.ListBuildingTypesForCreation;
using Kanstraction.OnSite.Application.Buildings.Queries.ListBuildingsForOperations;
using Kanstraction.OnSite.Application.Materials.Queries.ListMaterialCategoriesForAdmin;
using Kanstraction.OnSite.Application.Materials.Queries.ListMaterialsForAdmin;
using Kanstraction.OnSite.Application.Materials.Queries.GetMaterialDetails;

namespace Kanstraction.OnSite.Application.Abstractions;

public interface IAdminReadRepository
{
    Task<IReadOnlyList<MaterialCategoryListItemDto>> ListMaterialCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaterialListItemDto>> ListMaterialsAsync(CancellationToken cancellationToken = default);
    Task<MaterialDetailsDto?> GetMaterialDetailsAsync(
        int materialId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BuildingTypeListItemDto>> ListBuildingTypesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BuildingTypeCreationOptionDto>> ListBuildingTypesForCreationAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BuildingOperationsListItemDto>> ListBuildingsForOperationsAsync(int projectId, CancellationToken cancellationToken = default);
}
