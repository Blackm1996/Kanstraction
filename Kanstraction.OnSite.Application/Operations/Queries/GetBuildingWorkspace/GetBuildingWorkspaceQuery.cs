using Kanstraction.OnSite.Application.Common;
using MediatR;

namespace Kanstraction.OnSite.Application.Operations.Queries.GetBuildingWorkspace;

public sealed record OperationsSubStageDto(
    int Id,
    int StageId,
    string Name,
    int OrderIndex,
    WorkStatusDto Status,
    DateTime? StartDate,
    DateTime? EndDate,
    decimal LaborCost);

public sealed record OperationsStageDto(
    int Id,
    string Name,
    int OrderIndex,
    WorkStatusDto Status,
    double ProgressPercent,
    string OngoingSubStageName,
    DateTime? PaidDate,
    IReadOnlyList<OperationsSubStageDto> SubStages);

public sealed record BuildingWorkspaceDto(
    int BuildingId,
    IReadOnlyList<OperationsStageDto> Stages);

public sealed record GetBuildingWorkspaceQuery(int BuildingId)
    : IRequest<BuildingWorkspaceDto?>;
