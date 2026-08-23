using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.OnSite.Application.Buildings.Commands.CreateBuilding;

public sealed record CreateBuildingCommand(
    int ProjectId,
    int BuildingTypeId,
    string Code) : IStateChangingRequest<Result<CreateBuildingResult>>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result<CreateBuildingResult> response)
        => response.Value is { } building
            ? [new(
                OnSiteChangeScope.ProjectWork,
                OnSiteChangeKind.Created,
                ProjectId: ProjectId,
                BuildingId: building.BuildingId,
                EntityId: building.BuildingId)]
            : [];
}

public sealed record CreateBuildingResult(int BuildingId);
