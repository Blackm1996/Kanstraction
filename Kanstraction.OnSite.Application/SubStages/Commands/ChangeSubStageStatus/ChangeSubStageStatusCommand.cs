using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.OnSite.Application.Stages.Commands.Common;

namespace Kanstraction.OnSite.Application.SubStages.Commands.ChangeSubStageStatus;

public enum SubStageStatusAction
{
    Start,
    Finish,
    Reset
}

public sealed record ChangeSubStageStatusCommand(int SubStageId, SubStageStatusAction Action)
    : IStateChangingRequest<Result<WorkMutationResult>>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result<WorkMutationResult> response)
        => response.Value is { } work
            ? [new(
                OnSiteChangeScope.ProjectWork,
                OnSiteChangeKind.Updated,
                BuildingId: work.BuildingId,
                StageId: work.StageId,
                SubStageId: work.SubStageId,
                EntityId: work.SubStageId)]
            : [];
}
