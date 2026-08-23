using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.OnSite.Application.Services;

namespace Kanstraction.OnSite.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ApplicationServiceCollectionExtensions).Assembly);
            cfg.AddOpenBehavior(typeof(PublishOnSiteChangesBehavior<,>));
        });
        services.AddTransient<IProgressQueryService, ProgressQueryService>();
        return services;
    }
}
