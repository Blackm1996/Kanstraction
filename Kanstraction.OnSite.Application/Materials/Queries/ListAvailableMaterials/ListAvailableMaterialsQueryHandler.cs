using Kanstraction.OnSite.Application.Abstractions;
using MediatR;

namespace Kanstraction.OnSite.Application.Materials.Queries.ListAvailableMaterials;

public sealed class ListAvailableMaterialsQueryHandler(IOperationsReadRepository repository)
    : IRequestHandler<ListAvailableMaterialsQuery, IReadOnlyList<AvailableMaterialDto>>
{
    public Task<IReadOnlyList<AvailableMaterialDto>> Handle(
        ListAvailableMaterialsQuery request,
        CancellationToken cancellationToken)
        => repository.ListAvailableMaterialsAsync(request.SubStageId, cancellationToken);
}
