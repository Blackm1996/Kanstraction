using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Payments;
using Kanstraction.OnSite.Application.Payments.Commands.ResolvePayments;
using Kanstraction.OnSite.Domain.Entities;
using Xunit;

namespace Kanstraction.OnSite.Application.Tests;

public sealed class ResolvePaymentsHandlerTests
{
    [Fact]
    public async Task Generates_report_marks_finished_work_paid_and_commits_once()
    {
        var building = CreateBuilding();
        var renderer = new ReportRendererSpy();
        var unitOfWork = new UnitOfWorkSpy();
        var handler = new ResolvePaymentsCommandHandler(
            new ConstructionRepositoryStub([building]),
            renderer,
            unitOfWork,
            new ClockStub(new DateTime(2026, 7, 30, 14, 15, 0)));

        var result = await handler.Handle(
            new ResolvePaymentsCommand(7, "Site A", "payment.pdf"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.ResolvedSubStageCount);
        Assert.Equal(125m, result.Value.GrandTotal);
        Assert.Equal(WorkStatus.Paid, building.Stages.Single().SubStages.Single().Status);
        Assert.Equal(1, renderer.RenderCount);
        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.Equal("Site A", renderer.Report!.ProjectName);
    }

    [Fact]
    public async Task Does_not_render_or_commit_when_nothing_is_finished()
    {
        var building = CreateBuilding();
        building.Stages.Single().SubStages.Single().Status = WorkStatus.NotStarted;
        var renderer = new ReportRendererSpy();
        var unitOfWork = new UnitOfWorkSpy();
        var handler = new ResolvePaymentsCommandHandler(
            new ConstructionRepositoryStub([building]),
            renderer,
            unitOfWork,
            new ClockStub(DateTime.Today));

        var result = await handler.Handle(
            new ResolvePaymentsCommand(7, "Site A", "payment.pdf"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("payment.nothing_to_resolve", result.Error!.Code);
        Assert.Equal(0, renderer.RenderCount);
        Assert.Equal(0, unitOfWork.CommitCount);
    }

    private static Building CreateBuilding()
    {
        var building = new Building { Id = 1, ProjectId = 7, Code = "B-1" };
        var stage = new Stage
        {
            Id = 10,
            BuildingId = 1,
            Building = building,
            Name = "Structure",
            OrderIndex = 1,
            Status = WorkStatus.Finished
        };
        stage.MutableSubStages.Add(new SubStage
        {
            Id = 100,
            StageId = 10,
            Stage = stage,
            Name = "Concrete",
            OrderIndex = 1,
            LaborCost = 125m,
            Status = WorkStatus.Finished
        });
        building.MutableStages.Add(stage);
        building.Status = WorkStatus.Finished;
        return building;
    }

    private sealed class ReportRendererSpy : IPaymentReportRenderer
    {
        public int RenderCount { get; private set; }
        public PaymentReportData? Report { get; private set; }

        public Task RenderAsync(
            PaymentReportData report,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            RenderCount++;
            Report = report;
            return Task.CompletedTask;
        }
    }

    private sealed class UnitOfWorkSpy : IUnitOfWork
    {
        public int CommitCount { get; private set; }

        public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class ClockStub(DateTime now) : IClock
    {
        public DateTime Today => now.Date;
        public DateTime Now => now;
    }

    private sealed class ConstructionRepositoryStub(IReadOnlyList<Building> buildings)
        : IConstructionRepository
    {
        public Task<Building?> GetBuildingAggregateForStageStatusChangeAsync(
            int stageId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Building?>(null);

        public Task<Building?> GetBuildingAggregateForSubStageAsync(
            int subStageId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Building?>(null);

        public Task<IReadOnlyList<Building>> GetBuildingAggregatesForPaymentAsync(
            int projectId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(buildings);

        public Task<bool> BuildingCodeExistsAsync(
            int projectId,
            string code,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<Building?> GetBuildingAggregateAsync(
            int buildingId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(buildings.FirstOrDefault());

        public Task<Building?> GetBuildingAggregateForStopAsync(
            int buildingId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(buildings.FirstOrDefault());

        public void Add(Building building)
        {
        }

        public void Remove(Building building)
        {
        }
    }
}
