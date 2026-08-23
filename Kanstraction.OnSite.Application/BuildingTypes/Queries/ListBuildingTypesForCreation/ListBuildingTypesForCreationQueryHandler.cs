using Kanstraction.OnSite.Application.Abstractions;
using MediatR;

namespace Kanstraction.OnSite.Application.BuildingTypes.Queries.ListBuildingTypesForCreation;

public sealed class ListBuildingTypesForCreationQueryHandler(IAdminReadRepository repository)
    : IRequestHandler<ListBuildingTypesForCreationQuery, IReadOnlyList<BuildingTypeCreationOptionDto>>
{
    public Task<IReadOnlyList<BuildingTypeCreationOptionDto>> Handle(
        ListBuildingTypesForCreationQuery request,
        CancellationToken cancellationToken)
        => repository.ListBuildingTypesForCreationAsync(cancellationToken);
}
