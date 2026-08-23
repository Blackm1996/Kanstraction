using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Buildings;
using Kanstraction.OnSite.Application.Buildings.Commands.CreateBuilding;
using Kanstraction.OnSite.Domain.Entities;
using Xunit;

namespace Kanstraction.OnSite.Application.Tests;

public sealed class CreateBuildingHandlerTests
{
    [Fact]
    public async Task Expands_template_and_commits_the_complete_aggregate_once()
    {
        var repository = new RepositorySpy();
        var unitOfWork = new UnitOfWorkSpy();
        var template = new BuildingTemplateDefinition(
            4,
            [
                new BuildingStageTemplateDefinition(
                    2,
                    "Structure",
                    1,
                    [
                        new BuildingSubStageTemplateDefinition(
                            "Concrete",
                            1,
                            25m,
                            [new BuildingMaterialTemplateDefinition(9, 3.5m)])
                    ])
            ]);
        var handler = new CreateBuildingCommandHandler(
            repository,
            new TemplateReaderStub(template),
            unitOfWork,
            new ClockStub(new DateTime(2026, 7, 30)));

        var result = await handler.Handle(
            new CreateBuildingCommand(7, 4, " B-12 "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, unitOfWork.CommitCount);
        var building = Assert.IsType<Building>(repository.Added);
        Assert.Equal("B-12", building.Code);
        var subStage = Assert.Single(Assert.Single(building.Stages).SubStages);
        Assert.Equal(25m, subStage.LaborCost);
        Assert.Equal(3.5m, Assert.Single(subStage.MaterialUsages).Qty);
    }

    [Fact]
    public async Task Duplicate_code_does_not_expand_or_commit()
    {
        var repository = new RepositorySpy { CodeExists = true };
        var unitOfWork = new UnitOfWorkSpy();
        var handler = new CreateBuildingCommandHandler(
            repository,
            new TemplateReaderStub(new BuildingTemplateDefinition(4, [])),
            unitOfWork,
            new ClockStub(DateTime.Today));

        var result = await handler.Handle(
            new CreateBuildingCommand(7, 4, "B-12"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(repository.Added);
        Assert.Equal(0, unitOfWork.CommitCount);
    }

    private sealed class RepositorySpy : IConstructionRepository
    {
        public bool CodeExists { get; init; }
        public Building? Added { get; private set; }

        public Task<bool> BuildingCodeExistsAsync(int projectId, string code, CancellationToken cancellationToken = default)
            => Task.FromResult(CodeExists);

        public void Add(Building building) => Added = building;
        public void Remove(Building building) { }
        public Task<Building?> GetBuildingAggregateAsync(int buildingId, CancellationToken cancellationToken = default)
            => Task.FromResult<Building?>(null);
        public Task<Building?> GetBuildingAggregateForStopAsync(int buildingId, CancellationToken cancellationToken = default)
            => Task.FromResult<Building?>(null);
        public Task<Building?> GetBuildingAggregateForStageStatusChangeAsync(int stageId, CancellationToken cancellationToken = default)
            => Task.FromResult<Building?>(null);
        public Task<Building?> GetBuildingAggregateForSubStageAsync(int subStageId, CancellationToken cancellationToken = default)
            => Task.FromResult<Building?>(null);
        public Task<IReadOnlyList<Building>> GetBuildingAggregatesForPaymentAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Building>>([]);
    }

    private sealed class TemplateReaderStub(BuildingTemplateDefinition template) : IBuildingTemplateReader
    {
        public Task<BuildingTemplateDefinition?> GetAsync(int buildingTypeId, CancellationToken cancellationToken = default)
            => Task.FromResult<BuildingTemplateDefinition?>(template);
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
}
