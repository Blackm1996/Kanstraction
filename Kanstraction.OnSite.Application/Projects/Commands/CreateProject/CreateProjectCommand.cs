using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.OnSite.Application.Projects.Queries.ListProjects;

namespace Kanstraction.OnSite.Application.Projects.Commands.CreateProject;

public sealed record CreateProjectCommand(string Name, DateTime StartDate)
    : IStateChangingRequest<Result<ProjectListItemDto>>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result<ProjectListItemDto> response)
        => response.Value is { } project
            ? [new(
                OnSiteChangeScope.ProjectCatalog,
                OnSiteChangeKind.Created,
                ProjectId: project.Id,
                EntityId: project.Id)]
            : [];
}
