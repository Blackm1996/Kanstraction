using Kanstraction.Application.Abstractions;
using Kanstraction.Domain.Entities;
using MediatR;

namespace Kanstraction.Application.Projects.Queries.ListProjects;

public sealed class ListProjectsQueryHandler : IRequestHandler<ListProjectsQuery, IReadOnlyList<Project>>
{
    private readonly IProjectRepository _projectRepository;

    public ListProjectsQueryHandler(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<IReadOnlyList<Project>> Handle(ListProjectsQuery request, CancellationToken cancellationToken)
    {
        return await _projectRepository.GetAllOrderedByNameAsync(cancellationToken);
    }
}
