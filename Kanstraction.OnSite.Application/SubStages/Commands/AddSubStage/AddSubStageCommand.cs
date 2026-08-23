using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.OnSite.Application.Stages.Commands.Common;

namespace Kanstraction.OnSite.Application.SubStages.Commands.AddSubStage;

public sealed record AddSubStageCommand(
    int StageId,
    string Name,
    int OrderIndex,
    decimal LaborCost) : IStateChangingRequest<Result<WorkMutationResult>>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result<WorkMutationResult> response)
        => response.Value is { } work
            ? [new(
                OnSiteChangeScope.ProjectWork,
                OnSiteChangeKind.Created,
                BuildingId: work.BuildingId,
                StageId: work.StageId,
                SubStageId: work.SubStageId,
                EntityId: work.SubStageId)]
            : [];
}
