using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Stages.Commands.Common;
using Kanstraction.OnSite.Domain.Errors;
using MediatR;

namespace Kanstraction.OnSite.Application.Stages.Commands.DeleteStage;

public sealed class DeleteStageCommandHandler(
    IConstructionRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteStageCommand, Result<WorkMutationResult>>
{
    public async Task<Result<WorkMutationResult>> Handle(
        DeleteStageCommand request,
        CancellationToken cancellationToken)
    {
        var building = await repository.GetBuildingAggregateForStageStatusChangeAsync(
            request.StageId,
            cancellationToken);
        if (building == null)
            return Result<WorkMutationResult>.Failure(DomainErrorCodes.StageNotFound, "The stage was not found.");

        try
        {
            building.RemoveStage(request.StageId);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<WorkMutationResult>.Success(
                new WorkMutationResult(building.Id, request.StageId));
        }
        catch (DomainRuleViolationException ex)
        {
            return Result<WorkMutationResult>.Failure(ex.Code, ex.Message);
        }
    }
}
