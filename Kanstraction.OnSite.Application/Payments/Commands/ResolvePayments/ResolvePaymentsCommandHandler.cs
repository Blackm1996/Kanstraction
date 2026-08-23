using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Domain.Entities;
using MediatR;

namespace Kanstraction.OnSite.Application.Payments.Commands.ResolvePayments;

public sealed class ResolvePaymentsCommandHandler
    : IRequestHandler<ResolvePaymentsCommand, Result<PaymentResolutionResult>>
{
    private readonly IConstructionRepository _repository;
    private readonly IPaymentReportRenderer _reportRenderer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ResolvePaymentsCommandHandler(
        IConstructionRepository repository,
        IPaymentReportRenderer reportRenderer,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _repository = repository;
        _reportRenderer = reportRenderer;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<PaymentResolutionResult>> Handle(
        ResolvePaymentsCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DestinationPath))
            return Result<PaymentResolutionResult>.Failure(
                "payment.destination_required",
                "A report destination is required.");

        var buildings = await _repository.GetBuildingAggregatesForPaymentAsync(
            request.ProjectId,
            cancellationToken);

        var eligible = buildings
            .SelectMany(building => building.Stages)
            .SelectMany(stage => stage.SubStages)
            .Where(subStage => subStage.Status == WorkStatus.Finished)
            .ToList();

        if (eligible.Count == 0)
            return Result<PaymentResolutionResult>.Failure(
                "payment.nothing_to_resolve",
                "No completed sub-stages are available to resolve.");

        var reportBuildings = buildings
            .Select(building =>
            {
                var stageGroups = building.Stages
                    .OrderBy(stage => stage.OrderIndex)
                    .Select(stage =>
                    {
                        var rows = stage.SubStages
                            .Where(subStage => subStage.Status == WorkStatus.Finished)
                            .OrderBy(subStage => subStage.OrderIndex)
                            .Select(subStage => new PaymentSubStageRow(
                                stage.Name,
                                subStage.Name,
                                subStage.LaborCost))
                            .ToList();

                        return new PaymentStageGroup(
                            stage.Name,
                            rows,
                            rows.Sum(row => row.LaborCost));
                    })
                    .Where(group => group.Items.Count > 0)
                    .ToList();

                return new PaymentBuildingGroup(
                    building.Code,
                    stageGroups,
                    stageGroups.Sum(group => group.StageSubtotal));
            })
            .Where(group => group.Stages.Count > 0)
            .ToList();

        var grandTotal = reportBuildings.Sum(group => group.BuildingSubtotal);
        var report = new PaymentReportData(
            request.ProjectName,
            _clock.Now,
            reportBuildings,
            grandTotal);

        await _reportRenderer.RenderAsync(
            report,
            request.DestinationPath,
            cancellationToken);

        foreach (var building in buildings)
            building.ResolveFinishedWorkPayment(_clock.Today);

        await _unitOfWork.CommitAsync(cancellationToken);

        return Result<PaymentResolutionResult>.Success(
            new PaymentResolutionResult(eligible.Count, grandTotal));
    }
}
