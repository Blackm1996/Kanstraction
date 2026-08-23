using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.OnSite.Application.Stages.Commands.Common;

namespace Kanstraction.OnSite.Application.Stages.Commands.ChangeStageStatus;

public sealed record ChangeStageStatusCommand(int StageId, WorkStatusDto NewStatus)
    : IStateChangingRequest<Result<WorkMutationResult>>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result<WorkMutationResult> response)
        => response.Value is { } work
            ? [new(
                OnSiteChangeScope.ProjectWork,
                OnSiteChangeKind.Updated,
                BuildingId: work.BuildingId,
                StageId: work.StageId,
                EntityId: work.StageId)]
            : [];
}
