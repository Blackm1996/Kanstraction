using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows;
using Kanstraction.Data;
using Kanstraction.Services;

namespace Kanstraction;

public partial class App : Application
{
    public static BackupService BackupService { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var culture = new CultureInfo("fr-FR");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Resources/StringResources.xaml", UriKind.Relative) });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Resources/StringResources.fr.xaml", UriKind.Relative) });

        using (var db = new AppDbContext())
        {
            db.Database.EnsureCreated(); // safe because we use migrations already; fine for dev
            DbSeeder.Seed(db);
        }

        BackupService = new BackupService();
        try
        {
            BackupService.RunStartupMaintenanceAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Startup backup failed: {ex}");
        }

        base.OnStartup(e);
    }
}
