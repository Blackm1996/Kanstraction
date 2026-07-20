using Kanstraction.Application.Abstractions;
using MediatR;

namespace Kanstraction.Application.Materials.Queries.ListMaterialsForAdmin;

public sealed class ListMaterialsForAdminQueryHandler(IAdminReadRepository repository)
    : IRequestHandler<ListMaterialsForAdminQuery, IReadOnlyList<MaterialListItemDto>>
{
    public Task<IReadOnlyList<MaterialListItemDto>> Handle(ListMaterialsForAdminQuery request, CancellationToken cancellationToken)
        => repository.ListMaterialsAsync(cancellationToken);
}
