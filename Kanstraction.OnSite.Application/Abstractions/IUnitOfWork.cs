namespace Kanstraction.OnSite.Application.Abstractions;

public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);

    async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
        => await operation(cancellationToken);
}
