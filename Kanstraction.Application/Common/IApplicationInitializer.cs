namespace Kanstraction.Application.Common;

public interface IApplicationInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
