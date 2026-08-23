using Kanstraction.OnSite.Application.Abstractions;
using MediatR;

namespace Kanstraction.OnSite.Application.Projects.Queries.ListProjects;

public sealed class ListProjectsQueryHandler : IRequestHandler<ListProjectsQuery, IReadOnlyList<ProjectListItemDto>>
{
    private readonly IProjectRepository _projectRepository;

    public ListProjectsQueryHandler(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<IReadOnlyList<ProjectListItemDto>> Handle(ListProjectsQuery request, CancellationToken cancellationToken)
    {
        var projects = await _projectRepository.GetAllOrderedByNameAsync(cancellationToken);
        return projects
            .Select(project => new ProjectListItemDto(
                project.Id,
                project.Name,
                project.StartDate,
                project.EndDate))
            .ToList();
    }
}
