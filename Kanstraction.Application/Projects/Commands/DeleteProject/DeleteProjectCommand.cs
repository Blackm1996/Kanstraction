using MediatR;

namespace Kanstraction.Application.Projects.Commands.DeleteProject;

public sealed record DeleteProjectCommand(int ProjectId) : IRequest<bool>;
