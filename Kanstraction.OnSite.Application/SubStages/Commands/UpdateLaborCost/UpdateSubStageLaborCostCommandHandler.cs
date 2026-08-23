using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Domain.Errors;
using MediatR;

namespace Kanstraction.OnSite.Application.SubStages.Commands.UpdateLaborCost;

public sealed class UpdateSubStageLaborCostCommandHandler(
    IConstructionRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateSubStageLaborCostCommand, Result>
{
    public async Task<Result> Handle(
        UpdateSubStageLaborCostCommand request,
        CancellationToken cancellationToken)
    {
        var building = await repository.GetBuildingAggregateForSubStageAsync(
            request.SubStageId,
            cancellationToken);
        if (building == null)
            return Result.Failure(
                DomainErrorCodes.SubStageNotFound,
                "The sub-stage was not found.");

        try
        {
            building.UpdateSubStageLaborCost(request.SubStageId, request.LaborCost);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainRuleViolationException ex)
        {
            return Result.Failure(ex.Code, ex.Message);
        }
    }
}
