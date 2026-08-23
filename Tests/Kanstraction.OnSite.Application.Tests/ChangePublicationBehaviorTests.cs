using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.OnSite.Application.Materials.Commands.SaveMaterial;
using Xunit;

namespace Kanstraction.OnSite.Application.Tests;

public sealed class ChangePublicationBehaviorTests
{
    [Fact]
    public async Task SuccessfulStateChangingRequestPublishesAfterHandlerReturns()
    {
        var publisher = new RecordingPublisher();
        var behavior =
            new PublishOnSiteChangesBehavior<SaveMaterialCommand, Result<int>>(
                publisher);
        var command = new SaveMaterialCommand(
            null,
            "Cement",
            "bag",
            10m,
            DateTime.Today,
            true,
            1);
        var response = Result<int>.Success(42);

        var returned = await behavior.Handle(
            command,
            () => Task.FromResult(response),
            CancellationToken.None);

        Assert.Same(response, returned);
        var change = Assert.Single(publisher.Changes);
        Assert.Equal(OnSiteChangeScope.Materials, change.Scope);
        Assert.Equal(OnSiteChangeKind.Created, change.Kind);
        Assert.Equal(42, change.EntityId);
    }

    [Fact]
    public async Task FailedStateChangingRequestDoesNotPublish()
    {
        var publisher = new RecordingPublisher();
        var behavior =
            new PublishOnSiteChangesBehavior<SaveMaterialCommand, Result<int>>(
                publisher);
        var command = new SaveMaterialCommand(
            null,
            string.Empty,
            string.Empty,
            0m,
            DateTime.Today,
            true,
            1);
        var response = Result<int>.Failure("material.invalid", "Invalid");

        await behavior.Handle(
            command,
            () => Task.FromResult(response),
            CancellationToken.None);

        Assert.Empty(publisher.Changes);
    }

    private sealed class RecordingPublisher : IOnSiteChangePublisher
    {
        public List<OnSiteChange> Changes { get; } = [];

        public ValueTask PublishAsync(
            OnSiteChange change,
            CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            return ValueTask.CompletedTask;
        }
    }
}
