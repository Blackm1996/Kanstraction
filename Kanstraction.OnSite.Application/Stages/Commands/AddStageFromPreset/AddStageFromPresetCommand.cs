using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.OnSite.Application.Stages.Commands.Common;

namespace Kanstraction.OnSite.Application.Stages.Commands.AddStageFromPreset;

public sealed record AddStageFromPresetCommand(
    int BuildingId,
    int BuildingTypeId,
    int StagePresetId,
    int OrderIndex) : IStateChangingRequest<Result<WorkMutationResult>>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result<WorkMutationResult> response)
        => response.Value is { } work
            ? [new(
                OnSiteChangeScope.ProjectWork,
                OnSiteChangeKind.Created,
                BuildingId: work.BuildingId,
                StageId: work.StageId,
                EntityId: work.StageId)]
            : [];
}
