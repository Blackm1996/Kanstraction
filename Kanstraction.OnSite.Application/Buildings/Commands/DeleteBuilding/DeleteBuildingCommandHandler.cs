using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Domain.Errors;
using MediatR;

namespace Kanstraction.OnSite.Application.Buildings.Commands.DeleteBuilding;

public sealed class DeleteBuildingCommandHandler(
    IConstructionRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteBuildingCommand, Result>
{
    public async Task<Result> Handle(
        DeleteBuildingCommand request,
        CancellationToken cancellationToken)
    {
        var building = await repository.GetBuildingAggregateAsync(
            request.BuildingId,
            cancellationToken);

        if (building == null)
            return Result.Failure(
                DomainErrorCodes.BuildingNotFound,
                "The building was not found.");

        if (building.ContainsPaidWork())
            return Result.Failure(
                "building.contains_paid_work",
                "This building has paid items and cannot be deleted.");

        repository.Remove(building);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
