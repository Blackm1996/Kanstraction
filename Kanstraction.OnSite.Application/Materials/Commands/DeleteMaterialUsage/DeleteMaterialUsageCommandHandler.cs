using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Domain.Errors;
using MediatR;

namespace Kanstraction.OnSite.Application.Materials.Commands.DeleteMaterialUsage;

public sealed class DeleteMaterialUsageCommandHandler(
    IConstructionRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteMaterialUsageCommand, Result>
{
    public async Task<Result> Handle(
        DeleteMaterialUsageCommand request,
        CancellationToken cancellationToken)
    {
        var building = await repository.GetBuildingAggregateForSubStageAsync(
            request.SubStageId,
            cancellationToken);
        if (building == null)
            return Result.Failure(DomainErrorCodes.SubStageNotFound, "The sub-stage was not found.");

        try
        {
            building.RemoveMaterialUsage(request.SubStageId, request.MaterialUsageId);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainRuleViolationException ex)
        {
            return Result.Failure(ex.Code, ex.Message);
        }
    }
}
