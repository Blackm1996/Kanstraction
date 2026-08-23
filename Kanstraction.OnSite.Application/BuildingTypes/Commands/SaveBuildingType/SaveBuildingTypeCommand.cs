using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.OnSite.Application.BuildingTypes.Commands.SaveBuildingType;

public sealed record BuildingTypeMaterialDefinition(
    int MaterialId,
    decimal? Quantity);

public sealed record BuildingTypeSubStageDefinition(
    int SubStagePresetId,
    decimal? LaborCost,
    bool IncludeInReport,
    IReadOnlyList<BuildingTypeMaterialDefinition> Materials);

public sealed record BuildingTypeStageDefinition(
    int StagePresetId,
    int OrderIndex,
    IReadOnlyList<BuildingTypeSubStageDefinition> SubStages);

public sealed record BuildingTypeDefinition(
    int? Id,
    string Name,
    bool IsActive,
    IReadOnlyList<BuildingTypeStageDefinition> Stages);

public sealed record SaveBuildingTypeCommand(BuildingTypeDefinition Definition)
    : IStateChangingRequest<Result<int>>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result<int> response)
        => response.Value is > 0
            ? [new(
                OnSiteChangeScope.BuildingTypes,
                Definition.Id.HasValue
                    ? OnSiteChangeKind.Updated
                    : OnSiteChangeKind.Created,
                EntityId: response.Value)]
            : [];
}
