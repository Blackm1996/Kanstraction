using Kanstraction.OnSite.Application.Buildings.Commands.StopBuilding;

namespace Kanstraction.OnSite.Application.Reports.Models;

public abstract record OperationsExcelReport;

public sealed record CompensationExcelReport(CompensationReportData Data)
    : OperationsExcelReport;

public sealed record ProgressReportColumn(string Name);

public sealed record ProgressReportRow(
    string BuildingCode,
    IReadOnlyList<string> Values);

public sealed record ProgressReportSheet(
    string Name,
    IReadOnlyList<ProgressReportColumn> Columns,
    IReadOnlyList<ProgressReportRow> Rows);

public sealed record ProgressExcelReport(
    string ProjectName,
    IReadOnlyList<ProgressReportSheet> Sheets)
    : OperationsExcelReport;

public enum RemainingCostKind
{
    Labor,
    Material
}

public sealed record RemainingCostReportRow(
    string BuildingCode,
    IReadOnlyList<decimal> Values,
    decimal Total);

public sealed record RemainingCostReportSheet(
    string Name,
    IReadOnlyList<string> StageNames,
    IReadOnlyList<RemainingCostReportRow> Rows);

public sealed record RemainingCostExcelReport(
    string ProjectName,
    string Title,
    RemainingCostKind Kind,
    IReadOnlyList<RemainingCostReportSheet> Sheets)
    : OperationsExcelReport;

public sealed record StageSubStageReportRow(
    string Name,
    string Status,
    decimal LaborCost);

public sealed record StageSubStagesExcelReport(
    string ProjectName,
    string BuildingCode,
    string StageName,
    IReadOnlyList<StageSubStageReportRow> Rows)
    : OperationsExcelReport;

public sealed record MaterialReportRow(
    string? SubStageName,
    string MaterialName,
    string CategoryName,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal Total);

public sealed record MaterialsExcelReport(
    string ProjectName,
    string BuildingCode,
    string StageName,
    string? SubStageName,
    IReadOnlyList<MaterialReportRow> Rows)
    : OperationsExcelReport;
