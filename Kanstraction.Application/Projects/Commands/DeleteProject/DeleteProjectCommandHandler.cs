using Kanstraction.Application.Abstractions;
using MediatR;

namespace Kanstraction.Application.Projects.Commands.DeleteProject;

public sealed class DeleteProjectCommandHandler : IRequestHandler<DeleteProjectCommand, bool>
{
    private readonly IProjectRepository _projectRepository;

    public DeleteProjectCommandHandler(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<bool> Handle(DeleteProjectCommand request, CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdWithBuildingsAsync(request.ProjectId, cancellationToken);
        if (project == null)
            return false;

        _projectRepository.Remove(project);
        await _projectRepository.SaveChangesAsync(cancellationToken);
        return true;
    }
}
