namespace Kanstraction.Application.Abstractions;

public interface IDatabaseMigrator
{
    Task ApplyMigrationsAsync();
}
