using Kanstraction.Domain.Entities;
using MediatR;

namespace Kanstraction.Application.Projects.Queries.ListProjects;

public sealed record ListProjectsQuery : IRequest<IReadOnlyList<Project>>;
