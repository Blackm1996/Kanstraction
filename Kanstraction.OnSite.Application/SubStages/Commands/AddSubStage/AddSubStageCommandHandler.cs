using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Stages.Commands.Common;
using Kanstraction.OnSite.Domain.Errors;
using MediatR;

namespace Kanstraction.OnSite.Application.SubStages.Commands.AddSubStage;

public sealed class AddSubStageCommandHandler(
    IConstructionRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<AddSubStageCommand, Result<WorkMutationResult>>
{
    public async Task<Result<WorkMutationResult>> Handle(
        AddSubStageCommand request,
        CancellationToken cancellationToken)
    {
        var building = await repository.GetBuildingAggregateForStageStatusChangeAsync(
            request.StageId,
            cancellationToken);
        if (building == null)
            return Result<WorkMutationResult>.Failure(DomainErrorCodes.StageNotFound, "The stage was not found.");

        try
        {
            var subStage = building.AddSubStage(
                request.StageId,
                request.Name,
                request.OrderIndex,
                request.LaborCost);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<WorkMutationResult>.Success(
                new WorkMutationResult(building.Id, request.StageId, subStage.Id));
        }
        catch (DomainRuleViolationException ex)
        {
            return Result<WorkMutationResult>.Failure(ex.Code, ex.Message);
        }
    }
}
