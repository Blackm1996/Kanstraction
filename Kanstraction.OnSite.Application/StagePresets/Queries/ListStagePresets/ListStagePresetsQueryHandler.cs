using Kanstraction.OnSite.Application.Abstractions;
using MediatR;

namespace Kanstraction.OnSite.Application.StagePresets.Queries.ListStagePresets;

public sealed class ListStagePresetsQueryHandler(IStagePresetRepository repository)
    : IRequestHandler<ListStagePresetsQuery, IReadOnlyList<StagePresetListItemDto>>
{
    public Task<IReadOnlyList<StagePresetListItemDto>> Handle(
        ListStagePresetsQuery request,
        CancellationToken cancellationToken)
        => repository.ListAsync(cancellationToken);
}
