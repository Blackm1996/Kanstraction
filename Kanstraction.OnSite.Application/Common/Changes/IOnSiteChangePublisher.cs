namespace Kanstraction.OnSite.Application.Common.Changes;

public interface IOnSiteChangePublisher
{
    ValueTask PublishAsync(
        OnSiteChange change,
        CancellationToken cancellationToken = default);
}

public interface IOnSiteChangeFeed
{
    long GetRevision(OnSiteChangeScope scope);
}
