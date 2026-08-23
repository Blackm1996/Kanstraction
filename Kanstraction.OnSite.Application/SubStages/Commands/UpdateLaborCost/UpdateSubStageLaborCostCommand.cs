using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.OnSite.Application.SubStages.Commands.UpdateLaborCost;

public sealed record UpdateSubStageLaborCostCommand(
    int SubStageId,
    decimal LaborCost) : IStateChangingRequest<Result>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result response)
        => [new(
            OnSiteChangeScope.ProjectWork,
            OnSiteChangeKind.Updated,
            SubStageId: SubStageId,
            EntityId: SubStageId)];
}
