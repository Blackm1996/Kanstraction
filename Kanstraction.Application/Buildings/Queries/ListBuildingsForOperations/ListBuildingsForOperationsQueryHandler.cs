using Kanstraction.Application.Abstractions;
using MediatR;

namespace Kanstraction.Application.Buildings.Queries.ListBuildingsForOperations;

public sealed class ListBuildingsForOperationsQueryHandler(IAdminReadRepository repository)
    : IRequestHandler<ListBuildingsForOperationsQuery, IReadOnlyList<BuildingOperationsListItemDto>>
{
    public Task<IReadOnlyList<BuildingOperationsListItemDto>> Handle(ListBuildingsForOperationsQuery request, CancellationToken cancellationToken)
        => repository.ListBuildingsForOperationsAsync(request.ProjectId, cancellationToken);
}
