using Kanstraction.Application.Abstractions;
using Kanstraction.Infrastructure.Data;
using Kanstraction.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kanstraction.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddTransient<AppDbContext>();
        services.AddTransient<IConstructionRepository, EfConstructionRepository>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IDatabaseMigrator>(sp => new DatabaseMigrator(() => new AppDbContext()));
        return services;
    }
}
