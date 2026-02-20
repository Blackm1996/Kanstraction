using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Kanstraction.Application.Abstractions;
using Kanstraction.Application.Services;

namespace Kanstraction.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationServiceCollectionExtensions).Assembly));
        services.AddTransient<IProgressQueryService, ProgressQueryService>();
        return services;
    }
}
