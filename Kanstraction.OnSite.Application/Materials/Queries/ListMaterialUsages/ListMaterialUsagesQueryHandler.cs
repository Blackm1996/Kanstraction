using Kanstraction.OnSite.Application.Abstractions;
using MediatR;

namespace Kanstraction.OnSite.Application.Materials.Queries.ListMaterialUsages;

public sealed class ListMaterialUsagesQueryHandler(IOperationsReadRepository repository)
    : IRequestHandler<ListMaterialUsagesQuery, IReadOnlyList<MaterialUsageListItemDto>>
{
    public Task<IReadOnlyList<MaterialUsageListItemDto>> Handle(
        ListMaterialUsagesQuery request,
        CancellationToken cancellationToken)
        => repository.ListMaterialUsagesAsync(
            request.SubStageId,
            request.FreezePrices,
            cancellationToken);
}
