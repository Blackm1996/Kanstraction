using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.OnSite.Application.Projects.Commands.DeleteProject;

public sealed record DeleteProjectCommand(int ProjectId)
    : IStateChangingRequest<Result>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result response)
        => [new(
            OnSiteChangeScope.ProjectCatalog,
            OnSiteChangeKind.Deleted,
            ProjectId: ProjectId,
            EntityId: ProjectId)];
}
