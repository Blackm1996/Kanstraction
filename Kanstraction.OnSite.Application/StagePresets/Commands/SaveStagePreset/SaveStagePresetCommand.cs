using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.OnSite.Application.StagePresets.Commands.SaveStagePreset;

public sealed record StagePresetMaterialDefinition(
    int? UsageId,
    int MaterialId);

public sealed record StagePresetSubStageDefinition(
    int? Id,
    string Name,
    int OrderIndex,
    IReadOnlyList<StagePresetMaterialDefinition> Materials);

public sealed record StagePresetDefinition(
    int? Id,
    string Name,
    bool IsActive,
    IReadOnlyList<StagePresetSubStageDefinition> SubStages);

public sealed record SaveStagePresetCommand(StagePresetDefinition Definition)
    : IStateChangingRequest<Result<int>>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result<int> response)
        => response.Value is > 0
            ? [new(
                OnSiteChangeScope.StagePresets,
                Definition.Id.HasValue
                    ? OnSiteChangeKind.Updated
                    : OnSiteChangeKind.Created,
                EntityId: response.Value)]
            : [];
}
