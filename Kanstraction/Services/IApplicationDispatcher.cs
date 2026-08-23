using MediatR;

namespace Kanstraction.Services;

public interface IApplicationDispatcher
{
    Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}
