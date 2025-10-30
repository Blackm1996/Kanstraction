using Kanstraction.Application.Common;
using Kanstraction.Application.Operations;
using Kanstraction.Infrastructure.Data;
using Kanstraction.Infrastructure.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kanstraction.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKanstractionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var databasePath = AppDbContext.GetDefaultDbPath();
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(AppDbContext.BuildConnectionString(databasePath)));

        services.AddScoped<IApplicationInitializer, ApplicationInitializer>();
        services.AddTransient<IProjectCatalogService, ProjectCatalogService>();

        return services;
    }
}
