using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using MediatR;

namespace Kanstraction.OnSite.Application.Reports.Commands.ExportOperationsExcel;

public sealed class ExportOperationsExcelCommandHandler(
    IOperationsExcelRenderer renderer)
    : IRequestHandler<ExportOperationsExcelCommand, Result>
{
    public async Task<Result> Handle(
        ExportOperationsExcelCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            return Result.Failure(
                "report.destination_required",
                "A report destination is required.");
        }

        try
        {
            await renderer.RenderAsync(
                request.Report,
                request.DestinationPath,
                cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure("report.export_failed", ex.Message);
        }
    }
}
