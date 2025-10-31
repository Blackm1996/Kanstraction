using Kanstraction.Behaviors;
using Kanstraction.Infrastructure;
using Kanstraction.Presentation.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Kanstraction.Presentation.Wpf;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var culture = new CultureInfo("fr-FR");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        TextBoxEditHighlighter.Register();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddKanstractionInfrastructure(builder.Configuration);
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<MainWindow>();

        _host = builder.Build();

        await InitializeDatabaseAsync(_host.Services);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        if (mainWindow.DataContext is MainViewModel mainViewModel)
        {
            await mainViewModel.InitializeAsync();
        }
        else if (_host.Services.GetService<MainViewModel>() is { } vm)
        {
            await vm.InitializeAsync();
            mainWindow.DataContext = vm;
        }

        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static async Task InitializeDatabaseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<Kanstraction.Application.Common.IApplicationInitializer>();
        await initializer.InitializeAsync();
    }
}
