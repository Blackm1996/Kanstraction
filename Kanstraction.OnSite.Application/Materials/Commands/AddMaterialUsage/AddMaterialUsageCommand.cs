using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.OnSite.Application.Materials.Commands.AddMaterialUsage;

public sealed record AddMaterialUsageCommand(
    int SubStageId,
    int MaterialId,
    decimal Quantity) : IStateChangingRequest<Result>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result response)
        => [new(
            OnSiteChangeScope.ProjectWork,
            OnSiteChangeKind.Created,
            SubStageId: SubStageId,
            EntityId: SubStageId)];
}
