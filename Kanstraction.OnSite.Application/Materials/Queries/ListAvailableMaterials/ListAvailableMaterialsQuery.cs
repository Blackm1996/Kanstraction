using MediatR;

namespace Kanstraction.OnSite.Application.Materials.Queries.ListAvailableMaterials;

public sealed record AvailableMaterialDto(
    int Id,
    string Name,
    string Unit,
    string CategoryName);

public sealed record ListAvailableMaterialsQuery(int SubStageId)
    : IRequest<IReadOnlyList<AvailableMaterialDto>>;
