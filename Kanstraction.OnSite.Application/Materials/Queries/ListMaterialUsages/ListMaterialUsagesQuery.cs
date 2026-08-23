using MediatR;

namespace Kanstraction.OnSite.Application.Materials.Queries.ListMaterialUsages;

public sealed record MaterialUsageListItemDto(
    int Id,
    int SubStageId,
    string MaterialName,
    string CategoryName,
    string Unit,
    decimal Quantity,
    DateTime UsageDate,
    decimal DisplayUnitPrice);

public sealed record ListMaterialUsagesQuery(int SubStageId, bool FreezePrices)
    : IRequest<IReadOnlyList<MaterialUsageListItemDto>>;
