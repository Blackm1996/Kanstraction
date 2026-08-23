using Microsoft.Data.Sqlite;

namespace Kanstraction.OnSite.Infrastructure.Data;

internal static class OnSiteDatabaseLocation
{
    public static string GetDefaultDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(appData, "Kanstraction");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "app.db");
    }

    public static string GetDefaultConnectionString()
        => BuildConnectionString(GetDefaultDatabasePath());

    public static string BuildConnectionString(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        };

        return builder.ToString();
    }
}
