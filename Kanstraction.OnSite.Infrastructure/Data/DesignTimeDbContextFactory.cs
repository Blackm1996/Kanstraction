using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kanstraction.OnSite.Infrastructure.Data;
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OnSiteDbContext>
{
    public OnSiteDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OnSiteDbContext>()
            .UseSqlite(OnSiteDatabaseLocation.GetDefaultConnectionString())
            .Options;
        return new OnSiteDbContext(options);
    }
}
