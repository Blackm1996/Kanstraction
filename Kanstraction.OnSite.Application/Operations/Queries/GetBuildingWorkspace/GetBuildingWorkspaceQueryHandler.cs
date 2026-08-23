using Kanstraction.OnSite.Application.Abstractions;
using MediatR;

namespace Kanstraction.OnSite.Application.Operations.Queries.GetBuildingWorkspace;

public sealed class GetBuildingWorkspaceQueryHandler(IOperationsReadRepository repository)
    : IRequestHandler<GetBuildingWorkspaceQuery, BuildingWorkspaceDto?>
{
    public Task<BuildingWorkspaceDto?> Handle(
        GetBuildingWorkspaceQuery request,
        CancellationToken cancellationToken)
        => repository.GetBuildingWorkspaceAsync(
            request.BuildingId,
            cancellationToken);
}
