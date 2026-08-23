using MediatR;

namespace Kanstraction.OnSite.Application.Stages.Queries.GetStageAdditionOptions;

public sealed record StagePresetOptionDto(int Id, string Name);
public sealed record StageAdditionOptionsDto(
    int BuildingTypeId,
    int CurrentStageCount,
    IReadOnlyList<StagePresetOptionDto> AvailablePresets);
public sealed record GetStageAdditionOptionsQuery(int BuildingId)
    : IRequest<StageAdditionOptionsDto?>;
