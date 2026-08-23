using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Infrastructure.Data;
using Kanstraction.OnSite.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanstraction.OnSite.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddDbContextFactory<OnSiteDbContext>(
            options => options.UseSqlite(OnSiteDatabaseLocation.GetDefaultConnectionString()));
        services.AddScoped<IConstructionRepository, EfConstructionRepository>();
        services.AddScoped<IBuildingTemplateReader, EfBuildingTemplateReader>();
        services.AddScoped<IProjectRepository, EfProjectRepository>();
        services.AddScoped<IAdminReadRepository, EfAdminReadRepository>();
        services.AddScoped<IOperationsReadRepository, EfOperationsReadRepository>();
        services.AddScoped<IMaterialRepository, EfMaterialRepository>();
        services.AddScoped<IMaterialCategoryRepository, EfMaterialCategoryRepository>();
        services.AddScoped<IStagePresetRepository, EfStagePresetRepository>();
        services.AddScoped<IBuildingTypeRepository, EfBuildingTypeRepository>();
        services.AddScoped<IProgressDataReader, ProgressService>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IPaymentReportRenderer, PaymentReportRenderer>();
        services.AddScoped<IOperationsExcelRenderer, ClosedXmlOperationsExcelRenderer>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IDatabaseMigrator, DatabaseMigrator>();
        return services;
    }
}
