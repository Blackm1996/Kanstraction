using System.Windows;
using Kanstraction.Data;

namespace Kanstraction;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        using (var db = new AppDbContext())
        {
            db.Database.EnsureCreated(); // safe because we use migrations already; fine for dev
            DbSeeder.Seed(db);
        }
    }
}
