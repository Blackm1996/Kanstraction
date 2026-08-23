using Kanstraction.OnSite.Application.Abstractions;

namespace Kanstraction.OnSite.Infrastructure.Data;

public sealed class EfUnitOfWork(OnSiteDbContext dbContext) : IUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
