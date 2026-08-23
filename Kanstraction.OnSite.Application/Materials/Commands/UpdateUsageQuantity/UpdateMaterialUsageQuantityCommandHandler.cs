using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Domain.Errors;
using MediatR;

namespace Kanstraction.OnSite.Application.Materials.Commands.UpdateUsageQuantity;

public sealed class UpdateMaterialUsageQuantityCommandHandler(
    IConstructionRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateMaterialUsageQuantityCommand, Result>
{
    public async Task<Result> Handle(
        UpdateMaterialUsageQuantityCommand request,
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
            building.UpdateMaterialUsageQuantity(
                request.SubStageId,
                request.MaterialUsageId,
                request.Quantity);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainRuleViolationException ex)
        {
            return Result.Failure(ex.Code, ex.Message);
        }
    }
}
