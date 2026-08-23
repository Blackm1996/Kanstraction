using Kanstraction.Views;
using Kanstraction.Services;
using Kanstraction.ViewModels;
using Kanstraction.ViewModels.OnSite;
using Kanstraction.ViewModels.OnSite.Administration;
using Microsoft.Extensions.DependencyInjection;

namespace Kanstraction.DependencyInjection;

public static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddPresentationServices(this IServiceCollection services)
    {
        services.AddTransient<MainWindow>();
        services.AddTransient<OperationsView>();
        services.AddTransient<AdminHubView>();
        services.AddTransient<BackupHubView>();
        services.AddTransient<ProjectsExplorerViewModel>();
        services.AddTransient<OperationsWorkspaceViewModel>();
        services.AddTransient<AdministrationWorkspaceViewModel>();
        services.AddTransient<BackupWorkspaceViewModel>();
        services.AddTransient<StagePresetDesignerViewModel>();
        services.AddTransient<MaterialCatalogViewModel>();
        services.AddTransient<BuildingTypeDesignerViewModel>();
        services.AddTransient<StagePresetCatalogViewModel>();
        services.AddSingleton<IUserInteractionService, WpfUserInteractionService>();
        return services;
    }
}
