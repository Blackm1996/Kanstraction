using MediatR;

namespace Kanstraction.OnSite.Application.Projects.Queries.ListProjects;

public sealed record ProjectListItemDto(int Id, string Name, DateTime? StartDate, DateTime? EndDate);
public sealed record ListProjectsQuery : IRequest<IReadOnlyList<ProjectListItemDto>>;
