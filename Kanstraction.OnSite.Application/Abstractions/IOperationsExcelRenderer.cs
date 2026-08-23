using Kanstraction.OnSite.Application.Reports.Models;

namespace Kanstraction.OnSite.Application.Abstractions;

public interface IOperationsExcelRenderer
{
    Task RenderAsync(
        OperationsExcelReport report,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
