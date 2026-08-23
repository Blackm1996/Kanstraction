using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.OnSite.Application.Materials.Commands.DeleteMaterialUsage;

public sealed record DeleteMaterialUsageCommand(
    int SubStageId,
    int MaterialUsageId) : IStateChangingRequest<Result>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result response)
        => [new(
            OnSiteChangeScope.ProjectWork,
            OnSiteChangeKind.Deleted,
            SubStageId: SubStageId,
            EntityId: SubStageId)];
}
