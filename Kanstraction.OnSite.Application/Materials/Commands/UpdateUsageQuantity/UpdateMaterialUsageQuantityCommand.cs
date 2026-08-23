using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.OnSite.Application.Materials.Commands.UpdateUsageQuantity;

public sealed record UpdateMaterialUsageQuantityCommand(
    int SubStageId,
    int MaterialUsageId,
    decimal Quantity) : IStateChangingRequest<Result>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result response)
        => [new(
            OnSiteChangeScope.ProjectWork,
            OnSiteChangeKind.Updated,
            SubStageId: SubStageId,
            EntityId: SubStageId)];
}
