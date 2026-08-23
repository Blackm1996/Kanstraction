using Kanstraction.OnSite.Application.Abstractions;
using MediatR;

namespace Kanstraction.OnSite.Application.BuildingTypes.Queries.GetBuildingTypeEditor;

public sealed class GetBuildingTypeEditorQueryHandler(IBuildingTypeRepository repository)
    : IRequestHandler<GetBuildingTypeEditorQuery, BuildingTypeEditorWorkspaceDto?>
{
    public Task<BuildingTypeEditorWorkspaceDto?> Handle(
        GetBuildingTypeEditorQuery request,
        CancellationToken cancellationToken)
        => repository.GetEditorWorkspaceAsync(
            request.BuildingTypeId,
            cancellationToken);
}
