using System.Collections.Concurrent;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.Desktop;

public sealed class OnSiteChangeBus : IOnSiteChangePublisher, IOnSiteChangeFeed
{
    private readonly ConcurrentDictionary<OnSiteChangeScope, long> _revisions = new();

    public long GetRevision(OnSiteChangeScope scope)
        => _revisions.TryGetValue(scope, out var revision) ? revision : 0;

    public ValueTask PublishAsync(
        OnSiteChange change,
        CancellationToken cancellationToken = default)
    {
        _revisions.AddOrUpdate(change.Scope, 1, static (_, revision) => revision + 1);
        return ValueTask.CompletedTask;
    }
}
