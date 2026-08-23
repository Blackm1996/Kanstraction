using Kanstraction.OnSite.Application.Common;
using MediatR;

namespace Kanstraction.OnSite.Application.Reports.Queries.GetOperationsReportData;

public sealed record ReportPriceDto(
    DateTime StartDate,
    DateTime? EndDate,
    decimal PricePerUnit);

public sealed record ReportMaterialDto(
    int Id,
    string Name,
    string CategoryName,
    string Unit,
    decimal PricePerUnit,
    IReadOnlyList<ReportPriceDto> PriceHistory);

public sealed record ReportMaterialUsageDto(
    int Id,
    decimal Qty,
    DateTime UsageDate,
    ReportMaterialDto Material);

public sealed record ReportSubStageDto(
    int Id,
    string Name,
    int OrderIndex,
    WorkStatusDto Status,
    DateTime? StartDate,
    DateTime? EndDate,
    decimal LaborCost,
    IReadOnlyList<ReportMaterialUsageDto> MaterialUsages);

public sealed record ReportStageDto(
    int Id,
    string Name,
    int OrderIndex,
    WorkStatusDto Status,
    DateTime? StartDate,
    DateTime? EndDate,
    IReadOnlyList<ReportSubStageDto> SubStages);

public sealed record ReportBuildingDto(
    int Id,
    string Code,
    int? BuildingTypeId,
    string BuildingTypeName,
    WorkStatusDto Status,
    IReadOnlyList<ReportStageDto> Stages);

public sealed record ReportSubStageTemplateDto(
    int Id,
    string Name,
    int OrderIndex,
    bool? IncludeInReport);

public sealed record ReportStageTemplateDto(
    int Id,
    string Name,
    int OrderIndex,
    IReadOnlyList<ReportSubStageTemplateDto> SubStages);

public sealed record ReportBuildingTypeDto(
    int Id,
    string Name,
    IReadOnlyList<ReportStageTemplateDto> Stages);

public sealed record ProjectOperationsReportDataDto(
    int ProjectId,
    string ProjectName,
    IReadOnlyList<ReportBuildingTypeDto> BuildingTypes,
    IReadOnlyList<ReportBuildingDto> Buildings);

public sealed record GetProjectOperationsReportDataQuery(int ProjectId)
    : IRequest<ProjectOperationsReportDataDto?>;

public sealed record GetStageOperationsReportDataQuery(int StageId)
    : IRequest<ReportStageDto?>;

public sealed record GetSubStageOperationsReportDataQuery(int SubStageId)
    : IRequest<ReportSubStageDto?>;
