using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.SubStages.Commands.ChangeSubStageStatus;
using Kanstraction.OnSite.Domain.Entities;
using Xunit;

namespace Kanstraction.OnSite.Application.Tests;

public sealed class ChangeSubStageStatusHandlerTests
{
    [Fact]
    public async Task Handler_UsesClockAndCommitsOnce()
    {
        var building = CreateBuilding();
        var repository = new ConstructionRepositoryStub(building);
        var unitOfWork = new UnitOfWorkSpy();
        var clock = new ClockStub(new DateTime(2026, 7, 30));
        var handler = new ChangeSubStageStatusCommandHandler(repository, unitOfWork, clock);

        var result = await handler.Handle(
            new ChangeSubStageStatusCommand(101, SubStageStatusAction.Start),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.Equal(clock.Today, building.Stages.Single().SubStages.Single().StartDate);
    }

    [Fact]
    public async Task Handler_DoesNotCommitDomainFailure()
    {
        var building = CreateBuilding();
        building.Stages.Single().SubStages.Single().Status = WorkStatus.Finished;
        var unitOfWork = new UnitOfWorkSpy();
        var handler = new ChangeSubStageStatusCommandHandler(
            new ConstructionRepositoryStub(building),
            unitOfWork,
            new ClockStub(new DateTime(2026, 7, 30)));

        var result = await handler.Handle(
            new ChangeSubStageStatusCommand(101, SubStageStatusAction.Start),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, unitOfWork.CommitCount);
    }

    private static Building CreateBuilding()
    {
        var building = new Building { Id = 1, ProjectId = 1, Code = "A" };
        var stage = new Stage
        {
            Id = 10,
            Building = building,
            BuildingId = building.Id,
            Name = "Stage",
            OrderIndex = 1
        };
        stage.MutableSubStages.Add(new SubStage
        {
            Id = 101,
            Stage = stage,
            StageId = stage.Id,
            Name = "Sub-stage",
            OrderIndex = 1,
            LaborCost = 10
        });
        building.MutableStages.Add(stage);
        return building;
    }

    private sealed class ClockStub(DateTime today) : IClock
    {
        public DateTime Today => today;
        public DateTime Now => today;
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

    private sealed class ConstructionRepositoryStub(Building? building) : IConstructionRepository
    {
        public Task<Building?> GetBuildingAggregateForStageStatusChangeAsync(
            int stageId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(building);

        public Task<Building?> GetBuildingAggregateForSubStageAsync(
            int subStageId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(building);

        public Task<IReadOnlyList<Building>> GetBuildingAggregatesForPaymentAsync(
            int projectId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Building>>(
                building == null ? [] : [building]);

        public Task<bool> BuildingCodeExistsAsync(
            int projectId,
            string code,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<Building?> GetBuildingAggregateAsync(
            int buildingId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(building);

        public Task<Building?> GetBuildingAggregateForStopAsync(
            int buildingId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(building);

        public void Add(Building value)
        {
        }

        public void Remove(Building value)
        {
        }
    }
}
