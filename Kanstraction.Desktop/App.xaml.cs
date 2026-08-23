using System.Diagnostics;
using System.Globalization;
using System.Windows;
using Kanstraction.OnSite.Application.DependencyInjection;
using Kanstraction.OnSite.Application.Localization;
using Kanstraction.OnSite.Application.Startup;
using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.Shell;
using Kanstraction.Behaviors;
using Kanstraction.DependencyInjection;
using Kanstraction.OnSite.Infrastructure.DependencyInjection;
using Kanstraction.Localization;
using Kanstraction.Services;
using Kanstraction.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Kanstraction.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var culture = new CultureInfo("fr-FR");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        StringLocalizer.Configure(new ResourceDictionaryStringLocalizer());
        TextBoxEditHighlighter.Register();

        var loadingWindow = new StartupLoadingWindow();
        loadingWindow.UpdateStatus("Initialisation de la base de donnأ©es...");
        loadingWindow.Show();

        try
        {
            _services = BuildServiceProvider();

            loadingWindow.UpdateStatus("Application des migrations...");
            await _services.GetRequiredService<StartupInitializer>().InitializeAsync();

            loadingWindow.UpdateStatus("Dأ©marrage de l'application...");
            loadingWindow.Close();

            var mainWindow = _services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Application startup failed: {ex}");
            if (loadingWindow.IsVisible)
                loadingWindow.Close();

            MessageBox.Show(
                $"Une erreur est survenue lors de l'initialisation de l'application.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Kanstraction",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddInfrastructureServices();
        services.AddPresentationServices();
        services.AddTransient<StartupInitializer>();
        services.AddSingleton<OnSiteChangeBus>();
        services.AddSingleton<IOnSiteChangePublisher>(
            provider => provider.GetRequiredService<OnSiteChangeBus>());
        services.AddSingleton<IOnSiteChangeFeed>(
            provider => provider.GetRequiredService<OnSiteChangeBus>());
        services.AddSingleton<IDesktopModule, OnSiteDesktopModule>();
        services.AddSingleton<IApplicationDispatcher, ScopedApplicationDispatcher>();
        return services.BuildServiceProvider();
    }
}
