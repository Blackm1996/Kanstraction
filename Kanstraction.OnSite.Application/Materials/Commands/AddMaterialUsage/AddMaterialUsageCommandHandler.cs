using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Domain.Errors;
using MediatR;

namespace Kanstraction.OnSite.Application.Materials.Commands.AddMaterialUsage;

public sealed class AddMaterialUsageCommandHandler(
    IConstructionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<AddMaterialUsageCommand, Result>
{
    public async Task<Result> Handle(
        AddMaterialUsageCommand request,
        CancellationToken cancellationToken)
    {
        var building = await repository.GetBuildingAggregateForSubStageAsync(
            request.SubStageId,
            cancellationToken);
        if (building == null)
            return Result.Failure(DomainErrorCodes.SubStageNotFound, "The sub-stage was not found.");

        try
        {
            building.AddMaterialUsage(
                request.SubStageId,
                request.MaterialId,
                request.Quantity,
                clock.Today);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainRuleViolationException ex)
        {
            return Result.Failure(ex.Code, ex.Message);
        }
    }
}
