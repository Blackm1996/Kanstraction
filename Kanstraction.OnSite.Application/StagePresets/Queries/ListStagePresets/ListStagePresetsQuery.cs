using MediatR;

namespace Kanstraction.OnSite.Application.StagePresets.Queries.ListStagePresets;

public sealed record StagePresetListItemDto(
    int Id,
    string Name,
    bool IsActive,
    int SubStageCount);

public sealed record ListStagePresetsQuery
    : IRequest<IReadOnlyList<StagePresetListItemDto>>;
