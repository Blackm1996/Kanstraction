using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Stages.Commands.Common;
using Kanstraction.OnSite.Domain.Errors;
using MediatR;

namespace Kanstraction.OnSite.Application.SubStages.Commands.DeleteSubStage;

public sealed class DeleteSubStageCommandHandler(
    IConstructionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<DeleteSubStageCommand, Result<WorkMutationResult>>
{
    public async Task<Result<WorkMutationResult>> Handle(
        DeleteSubStageCommand request,
        CancellationToken cancellationToken)
    {
        var building = await repository.GetBuildingAggregateForSubStageAsync(
            request.SubStageId,
            cancellationToken);
        if (building == null)
            return Result<WorkMutationResult>.Failure(DomainErrorCodes.SubStageNotFound, "The sub-stage was not found.");

        try
        {
            var stageId = building.RemoveSubStage(request.SubStageId, clock.Today);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<WorkMutationResult>.Success(
                new WorkMutationResult(building.Id, stageId, request.SubStageId));
        }
        catch (DomainRuleViolationException ex)
        {
            return Result<WorkMutationResult>.Failure(ex.Code, ex.Message);
        }
    }
}
