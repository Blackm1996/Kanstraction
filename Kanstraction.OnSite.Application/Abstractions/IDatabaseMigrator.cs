namespace Kanstraction.OnSite.Application.Abstractions;

public interface IDatabaseMigrator
{
    Task ApplyMigrationsAsync();
}
