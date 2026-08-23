using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.OnSite.Application.Buildings.Commands.DeleteBuilding;

public sealed record DeleteBuildingCommand(int BuildingId)
    : IStateChangingRequest<Result>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result response)
        => [new(
            OnSiteChangeScope.ProjectWork,
            OnSiteChangeKind.Deleted,
            BuildingId: BuildingId,
            EntityId: BuildingId)];
}
