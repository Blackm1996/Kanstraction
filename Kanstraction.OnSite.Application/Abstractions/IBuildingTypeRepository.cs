using Kanstraction.OnSite.Application.BuildingTypes.Commands.SaveBuildingType;
using Kanstraction.OnSite.Application.BuildingTypes.Queries.GetBuildingTypeEditor;

namespace Kanstraction.OnSite.Application.Abstractions;

public interface IBuildingTypeRepository
{
    Task<BuildingTypeEditorWorkspaceDto?> GetEditorWorkspaceAsync(
        int? buildingTypeId,
        CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(
        string name,
        int? excludingBuildingTypeId,
        CancellationToken cancellationToken = default);

    Task<int> SaveAsync(
        BuildingTypeDefinition definition,
        CancellationToken cancellationToken = default);
}
