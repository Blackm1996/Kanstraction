using Kanstraction.OnSite.Application.Abstractions;
using MediatR;

namespace Kanstraction.OnSite.Application.Reports.Queries.GetOperationsReportData;

public sealed class GetProjectOperationsReportDataQueryHandler(
    IOperationsReadRepository repository)
    : IRequestHandler<GetProjectOperationsReportDataQuery, ProjectOperationsReportDataDto?>
{
    public Task<ProjectOperationsReportDataDto?> Handle(
        GetProjectOperationsReportDataQuery request,
        CancellationToken cancellationToken)
        => repository.GetProjectReportDataAsync(request.ProjectId, cancellationToken);
}

public sealed class GetStageOperationsReportDataQueryHandler(
    IOperationsReadRepository repository)
    : IRequestHandler<GetStageOperationsReportDataQuery, ReportStageDto?>
{
    public Task<ReportStageDto?> Handle(
        GetStageOperationsReportDataQuery request,
        CancellationToken cancellationToken)
        => repository.GetStageReportDataAsync(request.StageId, cancellationToken);
}

public sealed class GetSubStageOperationsReportDataQueryHandler(
    IOperationsReadRepository repository)
    : IRequestHandler<GetSubStageOperationsReportDataQuery, ReportSubStageDto?>
{
    public Task<ReportSubStageDto?> Handle(
        GetSubStageOperationsReportDataQuery request,
        CancellationToken cancellationToken)
        => repository.GetSubStageReportDataAsync(request.SubStageId, cancellationToken);
}
