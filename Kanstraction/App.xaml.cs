using System.Windows;
using Kanstraction.Data;
using System.Globalization;
using System.Threading;

namespace Kanstraction;

public partial class App : Application
{
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

        base.OnStartup(e);
    }
}
