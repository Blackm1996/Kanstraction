using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.OnSite.Application.Materials.Commands.CreateMaterialCategory;

public sealed record CreateMaterialCategoryCommand(string Name)
    : IStateChangingRequest<Result<int>>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result<int> response)
        => response.Value is > 0
            ? [new(
                OnSiteChangeScope.MaterialCategories,
                OnSiteChangeKind.Created,
                EntityId: response.Value)]
            : [];
}
