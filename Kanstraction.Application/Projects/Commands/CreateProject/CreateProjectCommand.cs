using Kanstraction.Domain.Entities;
using MediatR;

namespace Kanstraction.Application.Projects.Commands.CreateProject;

public sealed record CreateProjectCommand(string Name, DateTime StartDate) : IRequest<Project>;
