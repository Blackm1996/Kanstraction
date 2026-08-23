using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Projects.Queries.ListProjects;
using Kanstraction.OnSite.Domain.Entities;
using MediatR;

namespace Kanstraction.OnSite.Application.Projects.Commands.CreateProject;

public sealed class CreateProjectCommandHandler : IRequestHandler<CreateProjectCommand, Result<ProjectListItemDto>>
{
    private readonly IProjectRepository _projectRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateProjectCommandHandler(IProjectRepository projectRepository, IUnitOfWork unitOfWork)
    {
        _projectRepository = projectRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ProjectListItemDto>> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<ProjectListItemDto>.Failure("project.name_required", "Project name is required.");

        var project = Project.Create(request.Name, request.StartDate);

        await _projectRepository.AddAsync(project, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        return Result<ProjectListItemDto>.Success(
            new ProjectListItemDto(project.Id, project.Name, project.StartDate, project.EndDate));
    }
}
