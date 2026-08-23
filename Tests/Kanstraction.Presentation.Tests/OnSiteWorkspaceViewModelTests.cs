using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.ViewModels.OnSite;
using Xunit;

namespace Kanstraction.Presentation.Tests;

public sealed class OnSiteWorkspaceViewModelTests
{
    [Fact]
    public void Operations_workspace_prefers_full_refresh_over_material_refresh()
    {
        var changes = new ChangeFeedStub();
        var viewModel = new OperationsWorkspaceViewModel(changes);

        changes.Increment(OnSiteChangeScope.Materials);
        Assert.Equal(OperationsRefreshPlan.Materials, viewModel.GetRefreshPlan());

        changes.Increment(OnSiteChangeScope.ProjectWork);
        Assert.Equal(OperationsRefreshPlan.Workspace, viewModel.GetRefreshPlan());

        viewModel.CaptureCurrentRevisions();
        Assert.Equal(OperationsRefreshPlan.None, viewModel.GetRefreshPlan());
    }

    [Fact]
    public void Administration_material_change_invalidates_dependent_editors()
    {
        var changes = new ChangeFeedStub();
        var viewModel = new AdministrationWorkspaceViewModel(changes)
        {
            MaterialsLoaded = true,
            StagePresetsLoaded = true,
            BuildingTypesLoaded = true
        };

        changes.Increment(OnSiteChangeScope.Materials);
        var invalidateBuildingWorkspace = viewModel.ApplyRevisionInvalidations();

        Assert.True(invalidateBuildingWorkspace);
        Assert.False(viewModel.MaterialsLoaded);
        Assert.True(viewModel.StagePresetsLoaded);
        Assert.True(viewModel.StagePresetsStale);
        Assert.True(viewModel.BuildingTypesStale);
    }

    [Fact]
    public void Selection_versions_reject_superseded_async_requests()
    {
        var viewModel = new OperationsWorkspaceViewModel(new ChangeFeedStub());
        var first = viewModel.BeginBuildingSelection();
        var second = viewModel.BeginBuildingSelection();

        Assert.False(viewModel.IsCurrentBuildingSelection(first));
        Assert.True(viewModel.IsCurrentBuildingSelection(second));
    }

    private sealed class ChangeFeedStub : IOnSiteChangeFeed
    {
        private readonly Dictionary<OnSiteChangeScope, long> _revisions = [];

        public long GetRevision(OnSiteChangeScope scope) =>
            _revisions.GetValueOrDefault(scope);

        public void Increment(OnSiteChangeScope scope) =>
            _revisions[scope] = GetRevision(scope) + 1;
    }
}
