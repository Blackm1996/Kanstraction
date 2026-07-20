using Kanstraction.Domain.Entities;
using MediatR;

namespace Kanstraction.Application.Buildings.Queries.ListBuildingsForOperations;

public sealed record BuildingOperationsListItemDto(
    int Id,
    string Code,
    string TypeName,
    WorkStatus Status,
    int ProgressPercent,
    string CurrentStageName,
    bool HasPaidItems);

public sealed record ListBuildingsForOperationsQuery(int ProjectId) : IRequest<IReadOnlyList<BuildingOperationsListItemDto>>;
