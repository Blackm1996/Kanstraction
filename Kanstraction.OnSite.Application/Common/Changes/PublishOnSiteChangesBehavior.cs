using MediatR;

namespace Kanstraction.OnSite.Application.Common.Changes;

public sealed class PublishOnSiteChangesBehavior<TRequest, TResponse>(
    IOnSiteChangePublisher publisher)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        if (request is not IStateChangingRequest<TResponse> changingRequest ||
            response is not Result { IsSuccess: true })
        {
            return response;
        }

        foreach (var change in changingRequest.GetChanges(response))
            await publisher.PublishAsync(change, CancellationToken.None);

        return response;
    }
}
