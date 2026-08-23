using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.OnSite.Application.Materials.Commands.SaveMaterial;

public sealed record SaveMaterialCommand(
    int? MaterialId,
    string Name,
    string Unit,
    decimal PricePerUnit,
    DateTime EffectiveSince,
    bool IsActive,
    int MaterialCategoryId) : IStateChangingRequest<Result<int>>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result<int> response)
        => response.Value is > 0
            ? [new(
                OnSiteChangeScope.Materials,
                MaterialId.HasValue
                    ? OnSiteChangeKind.Updated
                    : OnSiteChangeKind.Created,
                EntityId: response.Value)]
            : [];
}
