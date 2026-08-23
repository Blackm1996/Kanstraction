using Kanstraction.OnSite.Domain.Entities;
using Kanstraction.OnSite.Domain.Errors;
using Kanstraction.OnSite.Domain.Services;
using Xunit;

namespace Kanstraction.OnSite.Domain.Tests;

public sealed class BuildingWorkflowTests
{
    private static readonly DateTime Today = new(2026, 7, 30);

    [Fact]
    public void StartSubStage_StartsAggregateAndSetsDate()
    {
        var building = CreateBuilding();

        building.StartSubStage(101, Today);

        Assert.Equal(WorkStatus.Ongoing, building.Status);
        Assert.Equal(WorkStatus.Ongoing, building.Stages.Single().Status);
        Assert.Equal(WorkStatus.Ongoing, building.Stages.Single().SubStages.First().Status);
        Assert.Equal(Today, building.Stages.Single().SubStages.First().StartDate);
    }

    [Fact]
    public void StartSubStage_RejectsSecondOngoingSubStage()
    {
        var building = CreateBuilding();
        building.StartSubStage(101, Today);

        var exception = Assert.Throws<DomainRuleViolationException>(
            () => building.StartSubStage(102, Today));

        Assert.Equal(DomainErrorCodes.AnotherSubStageIsOngoing, exception.Code);
    }

    [Fact]
    public void StartSubStage_RequiresPreviousSubStage()
    {
        var building = CreateBuilding();

        var exception = Assert.Throws<DomainRuleViolationException>(
            () => building.StartSubStage(102, Today));

        Assert.Equal(DomainErrorCodes.PreviousSubStageIncomplete, exception.Code);
    }

    [Fact]
    public void FinishSubStage_FreezesMaterialUsageDateAndRollsUpStatus()
    {
        var building = CreateBuilding(singleSubStage: true);
        building.StartSubStage(101, Today);

        building.FinishSubStage(101, Today.AddDays(2));

        var subStage = building.Stages.Single().SubStages.Single();
        Assert.Equal(WorkStatus.Finished, subStage.Status);
        Assert.Equal(Today.AddDays(2), subStage.EndDate);
        Assert.All(subStage.MaterialUsages, usage => Assert.Equal(Today.AddDays(2), usage.UsageDate));
        Assert.Equal(WorkStatus.Finished, building.Stages.Single().Status);
        Assert.Equal(WorkStatus.Finished, building.Status);
    }

    [Fact]
    public void ResetSubStage_ClearsDatesAndRollsBackAggregate()
    {
        var building = CreateBuilding(singleSubStage: true);
        building.StartSubStage(101, Today);

        building.ResetSubStage(101, Today.AddDays(1));

        var subStage = building.Stages.Single().SubStages.Single();
        Assert.Equal(WorkStatus.NotStarted, subStage.Status);
        Assert.Null(subStage.StartDate);
        Assert.Null(subStage.EndDate);
        Assert.Equal(WorkStatus.NotStarted, building.Status);
    }

    [Fact]
    public void ChangeStageStatus_StoppedPreservesLegacyWholeBuildingBehavior()
    {
        var building = CreateBuilding();

        building.ChangeStageStatus(10, WorkStatus.Stopped, Today);

        Assert.Equal(WorkStatus.Stopped, building.Status);
        Assert.All(
            building.Stages.SelectMany(stage => stage.SubStages),
            subStage => Assert.Equal(WorkStatus.Stopped, subStage.Status));
    }

    [Fact]
    public void StoppedWorkCountsAsComplete_ForCompatibility()
    {
        Assert.Equal(1d, WorkProgressRules.StatusToCompletion(WorkStatus.Stopped));
    }

    [Fact]
    public void PayingSubStageRequiresFinishedState()
    {
        var subStage = CreateBuilding(singleSubStage: true).Stages.Single().SubStages.Single();

        var exception = Assert.Throws<DomainRuleViolationException>(
            () => subStage.ApplyStatusTransition(WorkStatus.Paid, Today));

        Assert.Equal(DomainErrorCodes.InvalidSubStageTransition, exception.Code);
    }

    [Fact]
    public void NegativeCostAndQuantityAreRejected()
    {
        var subStage = CreateBuilding(singleSubStage: true).Stages.Single().SubStages.Single();
        var usage = subStage.MaterialUsages.Single();

        Assert.Equal(
            DomainErrorCodes.LaborCostNegative,
            Assert.Throws<DomainRuleViolationException>(() => subStage.UpdateLaborCost(-1)).Code);
        Assert.Equal(
            DomainErrorCodes.MaterialQuantityNegative,
            Assert.Throws<DomainRuleViolationException>(() => usage.UpdateQuantity(-1)).Code);
    }

    private static Building CreateBuilding(bool singleSubStage = false)
    {
        var building = new Building { Id = 1, ProjectId = 1, Code = "A-1" };
        var stage = new Stage
        {
            Id = 10,
            BuildingId = building.Id,
            Building = building,
            Name = "Structure",
            OrderIndex = 1
        };

        var first = new SubStage
        {
            Id = 101,
            StageId = stage.Id,
            Stage = stage,
            Name = "Foundation",
            OrderIndex = 1,
            LaborCost = 100
        };
        first.MutableMaterialUsages.Add(new MaterialUsage
        {
            Id = 1001,
            SubStageId = first.Id,
            SubStage = first,
            MaterialId = 1,
            Qty = 2,
            UsageDate = Today
        });

        stage.MutableSubStages.Add(first);
        if (!singleSubStage)
        {
            stage.MutableSubStages.Add(new SubStage
            {
                Id = 102,
                StageId = stage.Id,
                Stage = stage,
                Name = "Columns",
                OrderIndex = 2,
                LaborCost = 100
            });
        }

        building.MutableStages.Add(stage);
        return building;
    }
}
