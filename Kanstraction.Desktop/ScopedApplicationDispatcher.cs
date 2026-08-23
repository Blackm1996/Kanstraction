using Kanstraction.Services;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Kanstraction.Desktop;

public sealed class ScopedApplicationDispatcher(IServiceScopeFactory scopeFactory)
    : IApplicationDispatcher
{
    public async Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        return await sender.Send(request, cancellationToken);
    }
}
