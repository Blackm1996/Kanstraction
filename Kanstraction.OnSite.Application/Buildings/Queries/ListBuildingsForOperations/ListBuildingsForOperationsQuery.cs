using Kanstraction.OnSite.Application.Common;
using MediatR;

namespace Kanstraction.OnSite.Application.Buildings.Queries.ListBuildingsForOperations;

public sealed record BuildingOperationsListItemDto(
    int Id,
    string Code,
    string TypeName,
    WorkStatusDto Status,
    int ProgressPercent,
    string CurrentStageName,
    bool HasPaidItems);

public sealed record ListBuildingsForOperationsQuery(int ProjectId) : IRequest<IReadOnlyList<BuildingOperationsListItemDto>>;
