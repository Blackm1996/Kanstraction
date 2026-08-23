using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Domain.Entities;
using Kanstraction.OnSite.Domain.Errors;
using MediatR;

namespace Kanstraction.OnSite.Application.Buildings.Commands.StopBuilding;

public sealed class StopBuildingCommandHandler(
    IConstructionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<StopBuildingCommand, Result<CompensationReportData>>
{
    public async Task<Result<CompensationReportData>> Handle(
        StopBuildingCommand request,
        CancellationToken cancellationToken)
    {
        var building = await repository.GetBuildingAggregateForStopAsync(
            request.BuildingId,
            cancellationToken);
        if (building == null)
            return Result<CompensationReportData>.Failure(
                DomainErrorCodes.BuildingNotFound,
                "The building was not found.");

        var entries = building.Stages
            .OrderBy(stage => stage.OrderIndex)
            .SelectMany(stage => stage.SubStages
                .Where(subStage => subStage.Status is not (WorkStatus.Finished or WorkStatus.Paid))
                .OrderBy(subStage => subStage.OrderIndex)
                .Select(subStage => new CompensationReportEntry(
                    stage.Name,
                    subStage.Name,
                    stage.OrderIndex,
                    subStage.OrderIndex,
                    subStage.LaborCost,
                    CalculateMaterialCost(subStage))))
            .ToList();

        building.StopUnfinishedWork(clock.Today);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result<CompensationReportData>.Success(
            new CompensationReportData(building.Id, building.Code, entries));
    }

    private static decimal CalculateMaterialCost(SubStage subStage)
        => subStage.MaterialUsages.Sum(usage =>
            usage.Qty * ResolveUnitPrice(usage));

    private static decimal ResolveUnitPrice(MaterialUsage usage)
    {
        if (usage.UsageDate == default)
            return usage.Material.PricePerUnit;

        var date = usage.UsageDate.Date;
        return usage.Material.PriceHistory
            .Where(price =>
                price.StartDate.Date <= date &&
                (!price.EndDate.HasValue || price.EndDate.Value.Date >= date))
            .OrderByDescending(price => price.StartDate)
            .Select(price => (decimal?)price.PricePerUnit)
            .FirstOrDefault() ?? usage.Material.PricePerUnit;
    }
}
