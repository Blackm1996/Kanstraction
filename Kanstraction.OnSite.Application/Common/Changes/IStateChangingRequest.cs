using MediatR;

namespace Kanstraction.OnSite.Application.Common.Changes;

public interface IStateChangingRequest<TResponse> : IRequest<TResponse>
{
    IReadOnlyList<OnSiteChange> GetChanges(TResponse response);
}

public interface INonMutatingRequest<TResponse> : IRequest<TResponse>;
