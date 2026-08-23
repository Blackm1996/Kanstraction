using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.OnSite.Application.Stages.Commands.Common;

namespace Kanstraction.OnSite.Application.Stages.Commands.DeleteStage;

public sealed record DeleteStageCommand(int StageId)
    : IStateChangingRequest<Result<WorkMutationResult>>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result<WorkMutationResult> response)
        => response.Value is { } work
            ? [new(
                OnSiteChangeScope.ProjectWork,
                OnSiteChangeKind.Deleted,
                BuildingId: work.BuildingId,
                StageId: work.StageId,
                EntityId: work.StageId)]
            : [];
}
