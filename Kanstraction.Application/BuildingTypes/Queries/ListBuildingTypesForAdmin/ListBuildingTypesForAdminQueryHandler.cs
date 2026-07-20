using Kanstraction.Application.Abstractions;
using MediatR;

namespace Kanstraction.Application.BuildingTypes.Queries.ListBuildingTypesForAdmin;

public sealed class ListBuildingTypesForAdminQueryHandler(IAdminReadRepository repository)
    : IRequestHandler<ListBuildingTypesForAdminQuery, IReadOnlyList<BuildingTypeListItemDto>>
{
    public Task<IReadOnlyList<BuildingTypeListItemDto>> Handle(ListBuildingTypesForAdminQuery request, CancellationToken cancellationToken)
        => repository.ListBuildingTypesAsync(cancellationToken);
}
