using Kanstraction.OnSite.Application.Projects.Queries.ListProjects;
using Kanstraction.Services;
using Kanstraction.ViewModels;
using MediatR;
using Xunit;

namespace Kanstraction.Presentation.Tests;

public sealed class ProjectsExplorerViewModelTests
{
    [Fact]
    public async Task Refresh_selects_first_project_and_filter_is_case_insensitive()
    {
        var dispatcher = new DispatcherStub(
            [
                new ProjectListItemDto(1, "North Site", DateTime.Today, null),
                new ProjectListItemDto(2, "South Site", DateTime.Today, null)
            ]);
        var viewModel = new ProjectsExplorerViewModel(dispatcher);

        await viewModel.RefreshAsync();
        viewModel.SearchText = "south";

        Assert.False(viewModel.IsBusy);
        Assert.Equal(2, Assert.Single(viewModel.Projects).Id);
        Assert.Equal(2, viewModel.SelectedProject!.Id);
    }

    private sealed class DispatcherStub(IReadOnlyList<ProjectListItemDto> projects)
        : IApplicationDispatcher
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            object result = projects;
            return Task.FromResult((TResponse)result);
        }
    }
}
