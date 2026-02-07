using Kanstraction;
using Kanstraction.Converters;
using Kanstraction.Infrastructure.Data;
using Kanstraction.Domain.Entities;
using Kanstraction.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
namespace Kanstraction.Views;

public partial class OperationsView : UserControl
{
    private AppDbContext? _db;
    private Project? _currentProject;
    public string Breadcrumb
    {
        get => (string)GetValue(BreadcrumbProperty);
        set => SetValue(BreadcrumbProperty, value);
    }
    public static readonly DependencyProperty BreadcrumbProperty =
        DependencyProperty.Register(nameof(Breadcrumb), typeof(string), typeof(OperationsView),
            new PropertyMetadata(ResourceHelper.GetString("OperationsView_SelectProjectPrompt", "Select a project")));

    public string BuildingStatusFilterLabel
    {
        get => (string)GetValue(BuildingStatusFilterLabelProperty);
        set => SetValue(BuildingStatusFilterLabelProperty, value);
    }

    public static readonly DependencyProperty BuildingStatusFilterLabelProperty =
        DependencyProperty.Register(nameof(BuildingStatusFilterLabel), typeof(string), typeof(OperationsView),
            new PropertyMetadata(ResourceHelper.GetString("OperationsView_StatusFilter_All", "All statuses")));

    private int? _currentBuildingId;
    private int? _currentStageId;
    private SubStage? _editingSubStageForLabor;
    private decimal? _originalSubStageLabor;
    private MaterialUsage? _editingMaterialUsage;
    private decimal? _originalMaterialQuantity;
    private int? _pendingStageSelection;
    private List<BuildingRow> _buildingRows = new();
    private ICollectionView? _buildingView;
    private string _buildingSearchText = string.Empty;
    private readonly List<WorkStatus> _allWorkStatuses = Enum.GetValues(typeof(WorkStatus)).Cast<WorkStatus>().ToList();
    private HashSet<WorkStatus> _selectedStatusFilters = new();
    private bool _isUpdatingStatusCheckBoxes;
    private readonly WorkStatusToStringConverter _statusToStringConverter = new();

    private static readonly XLColor InfoLabelFillColor = XLColor.FromHtml("#D9EAF7");
    private static readonly XLColor InfoValueFillColor = XLColor.FromHtml("#F0F7FF");
    private static readonly XLColor InfoLabelFontColor = XLColor.FromHtml("#1F4E79");
    private static readonly XLColor SubStageHeaderFillColor = XLColor.FromHtml("#2F75B5");
    private static readonly XLColor SubStageHeaderFontColor = XLColor.White;
    private static readonly XLColor SubStageRowPrimaryFill = XLColor.FromHtml("#E6F0FA");
    private static readonly XLColor SubStageRowSecondaryFill = XLColor.FromHtml("#D2E3F6");
    private static readonly XLColor MaterialHeaderFillColor = XLColor.FromHtml("#7F7F7F");
    private static readonly XLColor MaterialHeaderFontColor = XLColor.White;
    private static readonly XLColor MaterialRowPrimaryFill = XLColor.FromHtml("#F2F2F2");
    private static readonly XLColor MaterialRowSecondaryFill = XLColor.FromHtml("#E0E0E0");
    private static readonly XLColor ProgressHeaderFillColor = XLColor.FromHtml("#C65911");
    private static readonly XLColor ProgressHeaderFontColor = XLColor.White;
    private static readonly XLColor ProgressRowPrimaryFill = XLColor.FromHtml("#FCE4D6");
    private static readonly XLColor ProgressRowSecondaryFill = XLColor.FromHtml("#F8CBAD");

    private sealed class BuildingRow
    {
        public int Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public WorkStatus Status { get; init; }
        public int ProgressPercent { get; init; }
        public string CurrentStageName { get; init; } = string.Empty;
        public bool HasPaidItems { get; init; }
    }

    private sealed class StageAssignmentInfo
    {
        public int StagePresetId { get; init; }
        public string StageName { get; init; } = string.Empty;
        public int Position { get; init; }
    }

    private sealed class ReportColumn
    {
        public int StagePresetId { get; init; }
        public string StageName { get; init; } = string.Empty;
        public int StagePosition { get; init; }
        public int SubStagePresetId { get; init; }
        public string SubStageName { get; init; } = string.Empty;
        public int SubStageOrderIndex { get; init; }
        public int SubStagePosition { get; init; }
    }

    private sealed class ProgressReportData
    {
        public string TypeName { get; init; } = string.Empty;
        public List<ReportColumn> Columns { get; init; } = new();
        public List<Building> Buildings { get; init; } = new();
    }

    private sealed class RemainingStageColumn
    {
        public string Name { get; init; } = string.Empty;
        public int OrderIndex { get; init; }
    }

    private sealed class RemainingCostReportData
    {
        public string ProjectName { get; init; } = string.Empty;
        public List<RemainingStageColumn> Columns { get; init; } = new();
        public List<Building> Buildings { get; init; } = new();
    }

    private enum RemainingCostReportType
    {
        Labor,
        Material
    }

    private sealed class CompensationEntry
    {
        public Stage Stage { get; init; } = null!;
        public SubStage SubStage { get; init; } = null!;
        public int StageOrder { get; init; }
        public int SubStageOrder { get; init; }
    }

    private static string FormatDecimal(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    private static string SanitizeFileName(string? input, string fallback)
    {
        if (string.IsNullOrWhiteSpace(fallback))
        {
            fallback = "Export";
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            return fallback;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(input.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static void TryOpenExportedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            };

            Process.Start(psi);
        }
        catch
        {
            // Ignored – the export succeeded, but opening the file failed.
        }
    }

    private static void WriteInfoRow(IXLWorksheet ws, int rowNumber, string label, string value)
    {
        var labelCell = ws.Cell(rowNumber, 1);
        var valueCell = ws.Cell(rowNumber, 2);

        labelCell.Value = label;
        labelCell.Style.Font.Bold = true;
        labelCell.Style.Font.FontColor = InfoLabelFontColor;
        labelCell.Style.Fill.BackgroundColor = InfoLabelFillColor;
        labelCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        labelCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

        valueCell.Value = value;
        valueCell.Style.Fill.BackgroundColor = InfoValueFillColor;
        valueCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        valueCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

        ws.Row(rowNumber).Height = 22;
    }

    private static void ApplyAlternatingRowStyles(IXLWorksheet ws, int startRow, int endRow, int startColumn, int endColumn, XLColor firstColor, XLColor secondColor)
    {
        if (endRow < startRow)
        {
            return;
        }

        for (int row = startRow; row <= endRow; row++)
        {
            var fillColor = ((row - startRow) % 2 == 0) ? firstColor : secondColor;
            var range = ws.Range(row, startColumn, row, endColumn);
            range.Style.Fill.BackgroundColor = fillColor;
            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }
    }

    private static string SanitizeWorksheetName(string? input, HashSet<string> existingNames)
    {
        var invalidChars = new HashSet<char>(new[] { '[', ']', '*', '?', '/', '\\', ':' });
        var baseName = string.IsNullOrWhiteSpace(input) ? "Sheet" : input.Trim();
        baseName = new string(baseName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "Sheet";
        }

        if (baseName.Length > 31)
        {
            baseName = baseName[..31];
        }

        var candidate = baseName;
        int counter = 1;
        while (existingNames.Contains(candidate))
        {
            var suffix = $"_{counter++}";
            var trimmedBase = baseName;
            if (trimmedBase.Length + suffix.Length > 31)
            {
                trimmedBase = trimmedBase[..(31 - suffix.Length)];
            }

            candidate = trimmedBase + suffix;
        }

        existingNames.Add(candidate);
        return candidate;
    }

    private static decimal CalculateMaterialTotal(SubStage subStage)
    {
        if (subStage.MaterialUsages == null)
        {
            return 0m;
        }

        decimal total = 0m;

        foreach (var usage in subStage.MaterialUsages)
        {
            if (usage == null || usage.Material == null)
            {
                continue;
            }

            var unitPrice = ComputeUnitPriceForUsage(usage, true);
            total += usage.Qty * unitPrice;
        }

        return total;
    }

    private Task ExportCompensationAsync(Building building, IReadOnlyList<CompensationEntry> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return Task.CompletedTask;
        }

        var titlePrefix = ResourceHelper.GetString("OperationsView_CompensationTitlePrefix", "Compensation lot");
        var lotLabel = ResourceHelper.GetString("OperationsView_LotLabel", "Lot");
        var title = $"{titlePrefix} {building.Code}".Trim();

        var sanitizedPrefix = SanitizeFileName(titlePrefix, "Compensation lot");
        var sanitizedCode = SanitizeFileName(building.Code, lotLabel);
        var defaultFileName = $"{sanitizedPrefix}_{sanitizedCode}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

        var sfd = new SaveFileDialog
        {
            Title = ResourceHelper.GetString("OperationsView_CompensationExportTitle", "Export compensation"),
            Filter = ResourceHelper.GetString("OperationsView_CompensationExportFilter", "Excel files (*.xlsx)|*.xlsx"),
            FileName = defaultFileName
        };

        var owner = Window.GetWindow(this);
        if (sfd.ShowDialog(owner) != true)
        {
            return Task.CompletedTask;
        }

        try
        {
            using var workbook = new XLWorkbook();
            var existingNames = new HashSet<string>();
            var worksheetLabel = ResourceHelper.GetString("OperationsView_CompensationWorksheetName", "Compensation");
            var sheetName = SanitizeWorksheetName(worksheetLabel, existingNames);
            var ws = workbook.Worksheets.Add(sheetName);

            var titleRange = ws.Range(1, 3, 1, 4);
            titleRange.Merge();
            titleRange.Value = title;
            titleRange.Style.Font.Bold = true;
            titleRange.Style.Font.FontSize = 16;
            titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(1).Height = 24;

            int headerRow = 3;
            var subStageHeader = ResourceHelper.GetString("OperationsView_SubStageLabel", "Sub-stage");
            var laborHeader = ResourceHelper.GetString("OperationsView_Labor", "Labor");
            var materialsHeader = ResourceHelper.GetString("OperationsView_MaterialsCost", "Materials cost");

            ws.Cell(headerRow, 1).Value = subStageHeader;
            ws.Cell(headerRow, 2).Value = laborHeader;
            ws.Cell(headerRow, 3).Value = materialsHeader;

            var headerRange = ws.Range(headerRow, 1, headerRow, 3);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Font.FontColor = SubStageHeaderFontColor;
            headerRange.Style.Fill.BackgroundColor = SubStageHeaderFillColor;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(headerRow).Height = 20;

            int currentRow = headerRow + 1;
            decimal totalLabor = 0m;
            decimal totalMaterials = 0m;

            foreach (var entry in entries
                         .OrderBy(e => e.StageOrder)
                         .ThenBy(e => e.SubStageOrder))
            {
                var stageName = entry.Stage.Name ?? string.Empty;
                var subStageName = entry.SubStage.Name ?? string.Empty;
                var nameValue = string.IsNullOrWhiteSpace(stageName)
                    ? subStageName
                    : $"{subStageName} ({stageName})";

                ws.Cell(currentRow, 1).Value = nameValue;

                ws.Cell(currentRow, 2).Value = entry.SubStage.LaborCost;
                var materialTotal = CalculateMaterialTotal(entry.SubStage);
                ws.Cell(currentRow, 3).Value = materialTotal;

                totalLabor += entry.SubStage.LaborCost;
                totalMaterials += materialTotal;

                currentRow++;
            }

            ws.Column(2).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(3).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(4).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Column(3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Column(4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            ApplyAlternatingRowStyles(ws, headerRow + 1, currentRow - 1, 1, 3, SubStageRowPrimaryFill, SubStageRowSecondaryFill);

            var totalRow = currentRow;
            ws.Cell(totalRow, 1).Value = ResourceHelper.GetString("OperationsView_Total", "Total");
            ws.Cell(totalRow, 1).Style.Font.Bold = true;
            ws.Cell(totalRow, 2).Value = totalLabor;
            ws.Cell(totalRow, 3).Value = totalMaterials;
            ws.Cell(totalRow, 4).Value = totalLabor + totalMaterials;

            var totalRange = ws.Range(totalRow, 1, totalRow, 4);
            totalRange.Style.Font.Bold = true;
            totalRange.Style.Fill.BackgroundColor = SubStageHeaderFillColor;
            totalRange.Style.Font.FontColor = SubStageHeaderFontColor;
            totalRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            totalRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(totalRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            ws.Columns(1, 4).AdjustToContents();

            workbook.SaveAs(sfd.FileName);
            TryOpenExportedFile(sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_CompensationExportFailedFormat", "Failed to export compensation:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    }

    private static DateTime? GetBuildingLastActivityDate(Building building)
    {
        DateTime? last = null;

        if (building.Stages == null)
        {
            return last;
        }

        foreach (var stage in building.Stages)
        {
            if (stage.EndDate.HasValue)
            {
                if (!last.HasValue || stage.EndDate.Value > last.Value)
                {
                    last = stage.EndDate.Value;
                }
            }

            if (stage.SubStages == null)
            {
                continue;
            }

            foreach (var subStage in stage.SubStages)
            {
                if (subStage.EndDate.HasValue)
                {
                    if (!last.HasValue || subStage.EndDate.Value > last.Value)
                    {
                        last = subStage.EndDate.Value;
                    }
                }
            }
        }

        return last;
    }

    private static DateTime? GetBuildingFirstActivityDate(Building building)
    {
        DateTime? first = null;

        if (building.Stages == null)
        {
            return first;
        }

        foreach (var stage in building.Stages)
        {
            if (stage.StartDate.HasValue)
            {
                if (!first.HasValue || stage.StartDate.Value < first.Value)
                {
                    first = stage.StartDate.Value;
                }
            }

            if (stage.SubStages == null)
            {
                continue;
            }

            foreach (var subStage in stage.SubStages)
            {
                if (subStage.StartDate.HasValue)
                {
                    if (!first.HasValue || subStage.StartDate.Value < first.Value)
                    {
                        first = subStage.StartDate.Value;
                    }
                }
            }
        }

        return first;
    }

    private static bool ShouldSkipBuilding(Building building, DateTime startDate, DateTime endDate)
    {
        var firstActivity = GetBuildingFirstActivityDate(building);
        if (firstActivity.HasValue && firstActivity.Value.Date > endDate.Date)
        {
            return true;
        }

        if (building.Status != WorkStatus.Paid && building.Status != WorkStatus.Stopped)
        {
            return false;
        }

        var lastActivity = GetBuildingLastActivityDate(building);
        if (!lastActivity.HasValue)
        {
            return false;
        }

        return lastActivity.Value.Date < startDate.Date;
    }

    private static Stage? FindStageForReport(Building building, ReportColumn column)
    {
        if (building.Stages == null)
        {
            return null;
        }

        var stages = building.Stages.OrderBy(s => s.OrderIndex).ToList();

        var stage = stages.FirstOrDefault(s => s.OrderIndex == column.StagePosition);
        if (stage != null)
        {
            return stage;
        }

        if (column.StagePosition > 0 && column.StagePosition <= stages.Count)
        {
            stage = stages[column.StagePosition - 1];
            if (stage != null)
            {
                return stage;
            }
        }

        stage = stages.FirstOrDefault(s => string.Equals(s.Name, column.StageName, StringComparison.OrdinalIgnoreCase));
        return stage;
    }

    private static SubStage? FindSubStageForReport(Stage? stage, ReportColumn column)
    {
        if (stage?.SubStages == null)
        {
            return null;
        }

        var subStages = stage.SubStages.OrderBy(ss => ss.OrderIndex).ToList();

        var subStage = subStages.FirstOrDefault(ss => ss.OrderIndex == column.SubStageOrderIndex);
        if (subStage != null)
        {
            return subStage;
        }

        if (column.SubStagePosition > 0 && column.SubStagePosition <= subStages.Count)
        {
            subStage = subStages[column.SubStagePosition - 1];
            if (subStage != null)
            {
                return subStage;
            }
        }

        subStage = subStages.FirstOrDefault(ss => string.Equals(ss.Name, column.SubStageName, StringComparison.OrdinalIgnoreCase));
        return subStage;
    }

    private static void WriteProgressWorksheet(IXLWorksheet ws, ProgressReportData report, string buildingHeader, string stoppedLabel, string projectName)
    {
        int totalColumns = Math.Max(4, report.Columns.Count + 1);

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            int projectStartColumn = Math.Min(3, totalColumns);
            int projectEndColumn = Math.Min(4, totalColumns);
            if (projectStartColumn <= projectEndColumn)
            {
                var projectRange = ws.Range(1, projectStartColumn, 1, projectEndColumn);
                projectRange.Merge();
                projectRange.Value = projectName;
                projectRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                projectRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                projectRange.Style.Font.Bold = true;
            }
        }

        ws.Row(1).Height = 22;

        ws.Cell(2, 1).Value = buildingHeader;
        for (int i = 0; i < report.Columns.Count; i++)
        {
            ws.Cell(2, i + 2).Value = report.Columns[i].SubStageName;
        }

        var headerRange = ws.Range(2, 1, 2, totalColumns);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontColor = ProgressHeaderFontColor;
        headerRange.Style.Fill.BackgroundColor = ProgressHeaderFillColor;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Alignment.WrapText = true;
        ws.Row(2).Height = 26;
        ws.SheetView.FreezeRows(2);

        int row = 3;
        foreach (var building in report.Buildings)
        {
            ws.Cell(row, 1).Value = building.Code;

            for (int colIndex = 0; colIndex < report.Columns.Count; colIndex++)
            {
                var column = report.Columns[colIndex];
                var stage = FindStageForReport(building, column);
                var subStage = FindSubStageForReport(stage, column);

                if (subStage == null)
                {
                    continue;
                }

                if (subStage.Status == WorkStatus.Paid)
                {
                    ws.Cell(row, colIndex + 2).Value = "100%";
                }
                else if (subStage.Status == WorkStatus.Stopped)
                {
                    ws.Cell(row, colIndex + 2).Value = $"100% ({stoppedLabel})";
                }
            }

            row++;
        }

        if (row > 3)
        {
            ApplyAlternatingRowStyles(ws, 3, row - 1, 1, totalColumns, ProgressRowPrimaryFill, ProgressRowSecondaryFill);
            ws.Rows(3, row - 1).Height = 22;
        }

        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 18);
        ws.Column(1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        for (int col = 2; col <= totalColumns; col++)
        {
            ws.Column(col).Width = Math.Max(ws.Column(col).Width, 24);
            ws.Column(col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Column(col).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        int lastRow = Math.Max(2, row - 1);
        var borderedRange = ws.Range(1, 1, lastRow, totalColumns);
        borderedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        borderedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    private static void WriteRemainingCostWorksheet(
        IXLWorksheet ws,
        RemainingCostReportData report,
        string buildingHeader,
        string totalHeader,
        string reportTitle,
        RemainingCostReportType reportType)
    {
        int totalColumns = Math.Max(4, report.Columns.Count + 2);

        if (!string.IsNullOrWhiteSpace(reportTitle))
        {
            int titleStartColumn = Math.Min(3, totalColumns);
            int titleEndColumn = Math.Min(4, totalColumns);
            if (titleStartColumn <= titleEndColumn)
            {
                var titleRange = ws.Range(1, titleStartColumn, 1, titleEndColumn);
                titleRange.Merge();
                titleRange.Value = reportTitle;
                titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                titleRange.Style.Font.Bold = true;
            }
        }

        ws.Row(1).Height = 22;

        ws.Cell(2, 1).Value = buildingHeader;
        for (int i = 0; i < report.Columns.Count; i++)
        {
            ws.Cell(2, i + 2).Value = report.Columns[i].Name;
        }
        ws.Cell(2, totalColumns).Value = totalHeader;

        var headerRange = ws.Range(2, 1, 2, totalColumns);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontColor = ProgressHeaderFontColor;
        headerRange.Style.Fill.BackgroundColor = ProgressHeaderFillColor;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Alignment.WrapText = true;
        ws.Row(2).Height = 26;
        ws.SheetView.FreezeRows(2);

        int row = 3;
        foreach (var building in report.Buildings)
        {
            ws.Cell(row, 1).Value = building.Code;
            decimal buildingTotal = 0m;

            for (int colIndex = 0; colIndex < report.Columns.Count; colIndex++)
            {
                var column = report.Columns[colIndex];
                var stage = FindStageForRemainingReport(building, column);
                var remaining = ComputeRemainingCost(stage, reportType);
                ws.Cell(row, colIndex + 2).Value = remaining;
                buildingTotal += remaining;
            }

            ws.Cell(row, totalColumns).Value = buildingTotal;
            row++;
        }

        if (row > 3)
        {
            ApplyAlternatingRowStyles(ws, 3, row - 1, 1, totalColumns, ProgressRowPrimaryFill, ProgressRowSecondaryFill);
            ws.Rows(3, row - 1).Height = 22;
        }

        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 18);
        ws.Column(1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        for (int col = 2; col <= totalColumns; col++)
        {
            ws.Column(col).Width = Math.Max(ws.Column(col).Width, 24);
            ws.Column(col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Column(col).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        for (int col = 2; col <= totalColumns; col++)
        {
            ws.Column(col).Style.NumberFormat.Format = "#,##0.00";
        }

        int lastRow = Math.Max(2, row - 1);
        var borderedRange = ws.Range(1, 1, lastRow, totalColumns);
        borderedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        borderedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    private static Stage? FindStageForRemainingReport(Building building, RemainingStageColumn column)
    {
        if (building.Stages == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(column.Name))
        {
            return building.Stages.FirstOrDefault(s =>
                string.Equals(s.Name, column.Name, StringComparison.OrdinalIgnoreCase));
        }

        return building.Stages.FirstOrDefault(s => s.OrderIndex == column.OrderIndex);
    }

    private static decimal ComputeRemainingCost(Stage? stage, RemainingCostReportType reportType)
    {
        if (stage == null)
        {
            return 0m;
        }

        if (stage.Status == WorkStatus.Paid || stage.Status == WorkStatus.Stopped)
        {
            return 0m;
        }

        decimal total = 0m;
        if (stage.SubStages == null)
        {
            return total;
        }

        foreach (var subStage in stage.SubStages)
        {
            if (subStage.Status == WorkStatus.Paid || subStage.Status == WorkStatus.Stopped)
            {
                continue;
            }

            if (reportType == RemainingCostReportType.Labor)
            {
                total += subStage.LaborCost;
                continue;
            }

            if (subStage.MaterialUsages == null)
            {
                continue;
            }

            bool freezePrices = subStage.Status == WorkStatus.Finished || subStage.Status == WorkStatus.Paid;
            foreach (var usage in subStage.MaterialUsages)
            {
                var unitPrice = ComputeUnitPriceForUsage(usage, freezePrices);
                total += usage.Qty * unitPrice;
            }
        }

        return total;
    }

    public OperationsView()
    {
        InitializeComponent();
        _selectedStatusFilters = _allWorkStatuses.ToHashSet();
        UpdateStatusFilterSummary();
    }

    public void SetDb(AppDbContext db) => _db = db;

    public async Task ShowProject(Project p)
    {
        if (_db == null) return;

        _currentProject = p;
        Breadcrumb = $"{p.Name}";

        // Clear right-side panels & selection state before loading
        StagesGrid.ItemsSource = null;
        SubStagesGrid.ItemsSource = null;
        MaterialsGrid.ItemsSource = null;
        _currentBuildingId = null;
        _currentStageId = null;
        UpdateSubStageLaborTotal(null);
        UpdateMaterialsTotal(null);

        // One source of truth for loading/selection of buildings
        await ReloadBuildingsAsync();  // (no specific selection; will select first if none)
    }

    private async void BuildingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_db == null) return;
        dynamic? b = BuildingsGrid.SelectedItem;
        if (b == null) return;

        _currentBuildingId = (int)b.Id;

        int? preferredStageId = _pendingStageSelection;
        _pendingStageSelection = null;

        await ReloadStagesAndSubStagesAsync((int)b.Id, preferredStageId);

        Breadcrumb = UpdateBreadcrumbWithBuilding(b.Code);
    }

    private void BuildingSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _buildingSearchText = (sender as TextBox)?.Text ?? string.Empty;
        RefreshBuildingView();
    }

    private async void StagesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_db == null) return;
        dynamic? s = StagesGrid.SelectedItem;
        if (s == null) return;

        _currentStageId = (int)s.Id;

        // Load tracked entities (NO AsNoTracking) so edits persist
        var subStages = await _db.SubStages
            .Where(ss => ss.StageId == _currentStageId)
            .OrderBy(ss => ss.OrderIndex)
            .ToListAsync();

        SubStagesGrid.ItemsSource = subStages;
        UpdateSubStageLaborTotal(subStages);
        var firstSub = subStages.FirstOrDefault();
        if (firstSub != null)
        {
            SubStagesGrid.SelectedItem = firstSub;
            SubStagesGrid.ScrollIntoView(firstSub);

            await LoadMaterialsForSubStageAsync(firstSub);
        }
        else
        {
            MaterialsGrid.ItemsSource = null;
            UpdateMaterialsTotal(null);
        }
    }

    private async void SubStagesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_db == null) return;
        var ss = SubStagesGrid.SelectedItem as SubStage;
        if (ss == null)
        {
            MaterialsGrid.ItemsSource = null;
            UpdateMaterialsTotal(null);
            return;
        }

        await LoadMaterialsForSubStageAsync(ss);
    }

    private async Task LoadMaterialsForSubStageAsync(SubStage subStage)
    {
        if (_db == null)
        {
            MaterialsGrid.ItemsSource = null;
            UpdateMaterialsTotal(null);
            return;
        }

        var usages = await _db.MaterialUsages
            .Include(mu => mu.Material)
                .ThenInclude(m => m.PriceHistory)
            .Include(mu => mu.Material)
                .ThenInclude(m => m.MaterialCategory)
            .Where(mu => mu.SubStageId == subStage.Id)
            .OrderBy(mu => mu.Material.Name)
            .ToListAsync();

        bool freezePrices = subStage.Status == WorkStatus.Finished || subStage.Status == WorkStatus.Paid;

        foreach (var usage in usages)
        {
            usage.DisplayUnitPrice = ComputeUnitPriceForUsage(usage, freezePrices);
        }

        MaterialsGrid.ItemsSource = usages;
        UpdateMaterialsTotal(usages);
    }

    private static decimal ComputeUnitPriceForUsage(MaterialUsage usage, bool freeze)
    {
        if (usage.Material == null)
        {
            return 0m;
        }

        if (!freeze || usage.UsageDate == default)
        {
            return usage.Material.PricePerUnit;
        }

        var usageDate = usage.UsageDate.Date;
        var history = usage.Material.PriceHistory;

        if (history != null && history.Count > 0)
        {
            var applicable = history
                .Where(h => h.StartDate.Date <= usageDate && (!h.EndDate.HasValue || h.EndDate.Value.Date >= usageDate))
                .OrderByDescending(h => h.StartDate)
                .FirstOrDefault();

            if (applicable != null)
            {
                return applicable.PricePerUnit;
            }

            var previous = history
                .Where(h => h.StartDate.Date <= usageDate)
                .OrderByDescending(h => h.StartDate)
                .FirstOrDefault();

            if (previous != null)
            {
                return previous.PricePerUnit;
            }

            var earliest = history
                .OrderBy(h => h.StartDate)
                .FirstOrDefault();

            if (earliest != null)
            {
                return earliest.PricePerUnit;
            }
        }

        return usage.Material.PricePerUnit;
    }

    private void UpdateSubStageLaborTotal(IEnumerable<SubStage>? subStages = null)
    {
        decimal total = 0m;
        var source = subStages ?? SubStagesGrid.ItemsSource as IEnumerable<SubStage>;
        if (source != null)
        {
            foreach (var subStage in source)
            {
                if (subStage != null)
                {
                    total += subStage.LaborCost;
                }
            }
        }

        SubStagesLaborTotalText.Text = FormatDecimal(total);
    }

    private void UpdateMaterialsTotal(IEnumerable<MaterialUsage>? usages = null)
    {
        decimal total = 0m;
        var source = usages ?? MaterialsGrid.ItemsSource as IEnumerable<MaterialUsage>;
        if (source != null)
        {
            foreach (var usage in source)
            {
                if (usage != null)
                {
                    total += usage.Qty * usage.DisplayUnitPrice;
                }
            }
        }

        MaterialsTotalText.Text = FormatDecimal(total);
    }

    private void ChangeStageStatus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.ContextMenu == null) return;

        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.Placement = PlacementMode.Bottom;
        btn.ContextMenu.IsOpen = true;
    }

    private async void StageStatusMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;
        if (sender is not MenuItem menuItem) return;
        if (menuItem.Tag is not WorkStatus newStatus) return;

        int stageId;
        try
        {
            dynamic ctx = menuItem.DataContext;
            stageId = (int)ctx.Id;
        }
        catch
        {
            return;
        }

        await ChangeStageStatusAsync(stageId, newStatus);
    }

    private async Task ChangeStageStatusAsync(int stageId, WorkStatus newStatus)
    {
        if (_db == null) return;

        try
        {
            var stage = await _db.Stages
                .Include(s => s.SubStages)
                    .ThenInclude(ss => ss.MaterialUsages)
                .Include(s => s.Building)
                    .ThenInclude(b => b.Stages)
                        .ThenInclude(st => st.SubStages)
                .FirstOrDefaultAsync(s => s.Id == stageId);

            if (stage == null)
                return;

            if (stage.SubStages != null)
            {
                foreach (var sub in stage.SubStages)
                {
                    sub.Status = newStatus;

                    switch (newStatus)
                    {
                        case WorkStatus.NotStarted:
                            sub.StartDate = null;
                            sub.EndDate = null;
                            break;
                        case WorkStatus.Ongoing:
                            if (sub.StartDate == null)
                                sub.StartDate = DateTime.Today;
                            sub.EndDate = null;
                            break;
                        case WorkStatus.Finished:
                        case WorkStatus.Paid:
                            if (sub.StartDate == null)
                                sub.StartDate = DateTime.Today;
                            sub.EndDate = DateTime.Today;
                            if (sub.MaterialUsages != null)
                            {
                                var freezeDate = sub.EndDate.Value.Date;
                                foreach (var usage in sub.MaterialUsages)
                                {
                                    usage.UsageDate = freezeDate;
                                }
                            }
                            break;
                        case WorkStatus.Stopped:
                            if (sub.StartDate == null)
                                sub.StartDate = DateTime.Today;
                            sub.EndDate = DateTime.Today;
                            break;
                    }
                }
            }

            stage.Status = newStatus;

            switch (newStatus)
            {
                case WorkStatus.NotStarted:
                    stage.StartDate = null;
                    stage.EndDate = null;
                    break;
                case WorkStatus.Ongoing:
                    if (stage.StartDate == null)
                        stage.StartDate = DateTime.Today;
                    stage.EndDate = null;
                    break;
                case WorkStatus.Finished:
                case WorkStatus.Paid:
                    if (stage.StartDate == null)
                        stage.StartDate = DateTime.Today;
                    stage.EndDate = DateTime.Today;
                    break;
                case WorkStatus.Stopped:
                    if (stage.StartDate == null)
                        stage.StartDate = DateTime.Today;
                    stage.EndDate = DateTime.Today;
                    break;
            }

            if (stage.Building != null)
            {
                if (newStatus == WorkStatus.Stopped)
                {
                    var today = DateTime.Today;

                    if (stage.Building.Stages != null)
                    {
                        foreach (var buildingStage in stage.Building.Stages)
                        {
                            buildingStage.Status = WorkStatus.Stopped;
                            if (buildingStage.StartDate == null)
                            {
                                buildingStage.StartDate = today;
                            }
                            buildingStage.EndDate = today;

                            if (buildingStage.SubStages != null)
                            {
                                foreach (var sub in buildingStage.SubStages)
                                {
                                    sub.Status = WorkStatus.Stopped;
                                    if (sub.StartDate == null)
                                    {
                                        sub.StartDate = today;
                                    }
                                    sub.EndDate = today;
                                }
                            }
                        }
                    }

                    stage.Building.Status = WorkStatus.Stopped;
                }
                else
                {
                    UpdateBuildingStatusFromStages(stage.Building);
                }
            }
            await _db.SaveChangesAsync();

            await ReloadBuildingsAsync(stage.BuildingId, stage.Id);
            await ReloadStagesAndSubStagesAsync(stage.BuildingId, stage.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_ChangeStageStatusFailedFormat", "Failed to change stage status:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    private void SubStagesGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.Column is DataGridTextColumn column &&
            column.Binding is Binding binding &&
            binding.Path?.Path == nameof(SubStage.LaborCost) &&
            e.Row.Item is SubStage ss)
        {
            _editingSubStageForLabor = ss;
            _originalSubStageLabor = ss.LaborCost;
        }
        else
        {
            _editingSubStageForLabor = null;
            _originalSubStageLabor = null;
        }
    }

    private async void SubStagesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_db == null) return;
        if (e.EditAction != DataGridEditAction.Commit)
        {
            if (e.Row.Item is SubStage subStage && ReferenceEquals(_editingSubStageForLabor, subStage))
            {
                _editingSubStageForLabor = null;
                _originalSubStageLabor = null;
            }

            return;
        }

        if (e.Column is DataGridTextColumn column &&
            column.Binding is Binding binding &&
            binding.Path?.Path == nameof(SubStage.LaborCost) &&
            e.Row.Item is SubStage ss)
        {
            decimal originalValue;
            if (_editingSubStageForLabor == ss && _originalSubStageLabor.HasValue)
            {
                originalValue = _originalSubStageLabor.Value;
            }
            else
            {
                originalValue = _db.Entry(ss).Property(s => s.LaborCost).OriginalValue;
            }

            if (ss.LaborCost < 0)
            {
                MessageBox.Show(
                    ResourceHelper.GetString("OperationsView_LaborCostNegative", "Labor cost cannot be negative."),
                    ResourceHelper.GetString("Common_ValidationTitle", "Validation"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ss.LaborCost = 0;
                if (e.EditingElement is TextBox negativeTextBox)
                {
                    negativeTextBox.Text = FormatDecimal(ss.LaborCost);
                }
            }

            var newValue = ss.LaborCost;

            if (originalValue == newValue)
            {
                _db.Entry(ss).Property(s => s.LaborCost).IsModified = false;
                _editingSubStageForLabor = null;
                _originalSubStageLabor = null;
                UpdateSubStageLaborTotal();
                return;
            }

            var message = string.Format(
                CultureInfo.CurrentCulture,
                ResourceHelper.GetString("OperationsView_ConfirmLaborChangeMessage", "Are you sure you want to change the labor from {0} to {1}?"),
                FormatDecimal(originalValue),
                FormatDecimal(newValue));

            var dialog = new ConfirmValueChangeDialog(
                ResourceHelper.GetString("OperationsView_ConfirmChangeTitle", "Save"),
                message,
                ResourceHelper.GetString("Common_Save", "Save"),
                ResourceHelper.GetString("OperationsView_ReturnToDefault", "Cancel"))
            {
                Owner = Window.GetWindow(this)
            };

            var result = dialog.ShowDialog();

            bool refreshTotal = false;
            if (result == true)
            {
                await _db.SaveChangesAsync();
                refreshTotal = true;
            }
            else
            {
                ss.LaborCost = originalValue;
                if (e.EditingElement is TextBox textBox)
                {
                    textBox.Text = FormatDecimal(originalValue);
                }

                var entry = _db.Entry(ss);
                entry.Property(s => s.LaborCost).CurrentValue = originalValue;
                entry.Property(s => s.LaborCost).IsModified = false;
                refreshTotal = true;
            }

            _editingSubStageForLabor = null;
            _originalSubStageLabor = null;

            if (refreshTotal)
            {
                UpdateSubStageLaborTotal();
            }
        }
    }

    private void MaterialsGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.Column is DataGridTextColumn column &&
            column.Binding is Binding binding &&
            binding.Path?.Path == nameof(MaterialUsage.Qty) &&
            e.Row.Item is MaterialUsage mu)
        {
            _editingMaterialUsage = mu;
            _originalMaterialQuantity = mu.Qty;
        }
        else
        {
            _editingMaterialUsage = null;
            _originalMaterialQuantity = null;
        }
    }

    private async void MaterialsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_db == null) return;
        if (e.EditAction != DataGridEditAction.Commit)
        {
            if (e.Row.Item is MaterialUsage materialUsage && ReferenceEquals(_editingMaterialUsage, materialUsage))
            {
                _editingMaterialUsage = null;
                _originalMaterialQuantity = null;
            }

            return;
        }

        if (e.Column is DataGridTextColumn column &&
            column.Binding is Binding binding &&
            binding.Path?.Path == nameof(MaterialUsage.Qty) &&
            e.Row.Item is MaterialUsage mu)
        {
            decimal originalValue;
            if (_editingMaterialUsage == mu && _originalMaterialQuantity.HasValue)
            {
                originalValue = _originalMaterialQuantity.Value;
            }
            else
            {
                originalValue = _db.Entry(mu).Property(m => m.Qty).OriginalValue;
            }

            if (e.EditingElement is TextBox qtyTextBox)
            {
                if (!NumberParsing.TryParseFlexibleDecimal(qtyTextBox.Text, out var parsedQty))
                {
                    MessageBox.Show(
                        ResourceHelper.GetString("OperationsView_InvalidQuantity", "Enter a valid quantity (>= 0)."),
                        ResourceHelper.GetString("Common_InvalidTitle", "Invalid"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    mu.Qty = originalValue;
                    qtyTextBox.Text = FormatDecimal(originalValue);
                    var entry = _db.Entry(mu);
                    entry.Property(m => m.Qty).CurrentValue = originalValue;
                    entry.Property(m => m.Qty).IsModified = false;
                    _editingMaterialUsage = null;
                    _originalMaterialQuantity = null;
                    return;
                }

                mu.Qty = parsedQty;
            }

            if (mu.Qty < 0)
            {
                MessageBox.Show(
                    ResourceHelper.GetString("OperationsView_QuantityNegative", "Quantity cannot be negative."),
                    ResourceHelper.GetString("Common_ValidationTitle", "Validation"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                mu.Qty = 0;
                if (e.EditingElement is TextBox negativeTextBox)
                {
                    negativeTextBox.Text = FormatDecimal(mu.Qty);
                }
            }

            var newValue = mu.Qty;

            if (originalValue == newValue)
            {
                _db.Entry(mu).Property(m => m.Qty).IsModified = false;
                _editingMaterialUsage = null;
                _originalMaterialQuantity = null;
                UpdateMaterialsTotal();
                return;
            }

            var message = string.Format(
                CultureInfo.CurrentCulture,
                ResourceHelper.GetString("OperationsView_ConfirmMaterialChangeMessage", "Are you sure you want to change the quantity from {0} to {1}?"),
                FormatDecimal(originalValue),
                FormatDecimal(newValue));

            var dialog = new ConfirmValueChangeDialog(
                ResourceHelper.GetString("OperationsView_ConfirmChangeTitle", "Confirm change"),
                message,
                ResourceHelper.GetString("Common_Save", "Save"),
                ResourceHelper.GetString("OperationsView_ReturnToDefault", "Return to default"))
            {
                Owner = Window.GetWindow(this)
            };

            var result = dialog.ShowDialog();

            bool refreshTotal = false;
            if (result == true)
            {
                await _db.SaveChangesAsync();
                refreshTotal = true;
            }
            else
            {
                mu.Qty = originalValue;
                if (e.EditingElement is TextBox textBox)
                {
                    textBox.Text = FormatDecimal(originalValue);
                }

                var entry = _db.Entry(mu);
                entry.Property(m => m.Qty).CurrentValue = originalValue;
                entry.Property(m => m.Qty).IsModified = false;
                refreshTotal = true;
            }

            _editingMaterialUsage = null;
            _originalMaterialQuantity = null;

            if (refreshTotal)
            {
                UpdateMaterialsTotal();
            }
        }
    }
    private string UpdateBreadcrumbWithBuilding(string code)
    {
        var baseText = Breadcrumb;
        var idx = baseText.IndexOf(" > ", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) baseText = baseText.Substring(0, idx);
        return $"{baseText} > {code}";
    }

    private async void AddBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectProjectFirst", "Select a project first."),
                ResourceHelper.GetString("OperationsView_NoProjectTitle", "No project"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Dialog to collect code + building type
        var dlg = new AddBuildingDialog(_db) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        var code = dlg.BuildingCode?.Trim();
        var typeId = dlg.BuildingTypeId;
        if (string.IsNullOrWhiteSpace(code) || typeId == null) return;

        // Enforce unique code per project (nice safeguard)
        var exists = await _db.Buildings.AnyAsync(b => b.ProjectId == _currentProject.Id && b.Code == code);
        if (exists)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_DuplicateCodeMessage", "A building with this code already exists in this project."),
                ResourceHelper.GetString("OperationsView_DuplicateCodeTitle", "Duplicate code"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // 1) Create the building
            var building = new Building
            {
                ProjectId = _currentProject.Id,
                BuildingTypeId = typeId.Value,
                Code = code!,
                Status = WorkStatus.NotStarted
            };
            _db.Buildings.Add(building);

            // 2) Get ordered stage preset ids for the selected building type
            var stagePresetIds = await _db.BuildingTypeStagePresets
                .Where(x => x.BuildingTypeId == typeId.Value)
                .OrderBy(x => x.OrderIndex)
                .Select(x => x.StagePresetId)
                .ToListAsync();

            var laborLookup = await _db.BuildingTypeSubStageLabors
                .Where(x => x.BuildingTypeId == typeId.Value && x.LaborCost.HasValue)
                .ToDictionaryAsync(x => x.SubStagePresetId, x => x.LaborCost!.Value);

            var materialLookup = await _db.BuildingTypeMaterialUsages
                .Where(x => x.BuildingTypeId == typeId.Value && x.Qty.HasValue)
                .ToDictionaryAsync(x => (x.SubStagePresetId, x.MaterialId), x => x.Qty!.Value);

            int stageOrder = 1;

            foreach (var presetId in stagePresetIds)
            {
                // Load the stage preset name
                var stageName = await _db.StagePresets
                    .Where(p => p.Id == presetId)
                    .Select(p => p.Name)
                    .FirstAsync();

                // Create Stage (attach to building via navigation; EF will set FK)
                var stage = new Stage
                {
                    Building = building,
                    Name = stageName,
                    OrderIndex = stageOrder++,
                    Status = WorkStatus.NotStarted
                };
                _db.Stages.Add(stage);

                // Load sub-stage presets for this stage preset (ordered)
                var subPresets = await _db.SubStagePresets
                    .Where(s => s.StagePresetId == presetId)
                    .OrderBy(s => s.OrderIndex)
                    .ToListAsync();

                foreach (var sp in subPresets)
                {
                    // Create SubStage
                    var sub = new SubStage
                    {
                        Stage = stage,
                        Name = sp.Name,
                        OrderIndex = sp.OrderIndex,
                        Status = WorkStatus.NotStarted,
                        LaborCost = laborLookup.TryGetValue(sp.Id, out var labor)
                            ? labor
                            : 0m
                    };
                    _db.SubStages.Add(sub);

                    // Copy MATERIALS from MaterialUsagePreset → MaterialUsage
                    var muPresets = await _db.MaterialUsagesPreset
                        .Where(mu => mu.SubStagePresetId == sp.Id)
                        .ToListAsync();

                    foreach (var mup in muPresets)
                    {
                        var qty = materialLookup.TryGetValue((sp.Id, mup.MaterialId), out var btQty)
                            ? btQty
                            : 0m;

                        // Seed with today's date; you can edit later
                        var mu = new MaterialUsage
                        {
                            SubStage = sub,                  // link via navigation
                            MaterialId = mup.MaterialId,
                            Qty = qty,
                            UsageDate = DateTime.Today,
                            Notes = null
                        };
                        _db.MaterialUsages.Add(mu);
                    }
                }
            }

            // 3) Commit all inserts in one shot
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            // 4) Refresh grid and select new building
            await ReloadBuildingsAsync(building.Id);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_CreateBuildingFailedFormat", "Failed to create building:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ReloadBuildingsAsync(int? selectBuildingId = null, int? preferredStageId = null)
    {
        if (_currentProject == null || _db == null)
        {
            return;
        }

        var buildings = await _db.Buildings
            .Where(b => b.ProjectId == _currentProject.Id)
            .Include(b => b.BuildingType)
            .Include(b => b.Stages)
                .ThenInclude(s => s.SubStages)
            .AsNoTracking()
            .ToListAsync();

        _buildingRows = buildings
            .Select(b => new BuildingRow
            {
                Id = b.Id,
                Code = b.Code,
                TypeName = b.BuildingType?.Name ?? string.Empty,
                Status = b.Status,
                ProgressPercent = ComputeBuildingProgress(b),
                CurrentStageName = ComputeCurrentStageName(b),
                HasPaidItems = BuildingHasPaidItems(b)
            })
            .OrderBy(x => x.Id)
            .ToList();

        _buildingView = CollectionViewSource.GetDefaultView(_buildingRows);
        if (_buildingView != null)
        {
            _buildingView.Filter = BuildingFilter;
            BuildingsGrid.ItemsSource = _buildingView;
        }
        else
        {
            BuildingsGrid.ItemsSource = _buildingRows;
        }

        BuildingRow? desiredSelection = null;
        if (selectBuildingId.HasValue)
        {
            desiredSelection = _buildingRows.FirstOrDefault(x => x.Id == selectBuildingId.Value);
        }

        RefreshBuildingView(desiredSelection, preferredStageId);
    }

    private void RefreshBuildingView(BuildingRow? desiredSelection = null, int? preferredStageId = null)
    {
        if (_buildingView == null)
        {
            BuildingsGrid.ItemsSource = _buildingRows;

            if (desiredSelection != null)
            {
                _pendingStageSelection = preferredStageId;
                BuildingsGrid.SelectedItem = desiredSelection;
                BuildingsGrid.ScrollIntoView(desiredSelection);
                return;
            }

            if (BuildingsGrid.SelectedItem == null && _buildingRows.Count > 0)
            {
                _pendingStageSelection = null;
                BuildingsGrid.SelectedItem = _buildingRows[0];
                BuildingsGrid.ScrollIntoView(_buildingRows[0]);
                return;
            }

            if (BuildingsGrid.SelectedItem == null)
            {
                StagesGrid.ItemsSource = null;
                SubStagesGrid.ItemsSource = null;
                MaterialsGrid.ItemsSource = null;
                _currentBuildingId = null;
                _currentStageId = null;
                UpdateSubStageLaborTotal(null);
                UpdateMaterialsTotal(null);
            }

            return;
        }

        _buildingView.Refresh();
        var filtered = _buildingView.Cast<BuildingRow>().ToList();

        if (desiredSelection != null)
        {
            if (filtered.Contains(desiredSelection))
            {
                _pendingStageSelection = preferredStageId;
                BuildingsGrid.SelectedItem = desiredSelection;
                BuildingsGrid.ScrollIntoView(desiredSelection);
                return;
            }

            _pendingStageSelection = null;
        }

        if (filtered.Count == 0)
        {
            BuildingsGrid.SelectedItem = null;
            StagesGrid.ItemsSource = null;
            SubStagesGrid.ItemsSource = null;
            MaterialsGrid.ItemsSource = null;
            _currentBuildingId = null;
            _currentStageId = null;
            UpdateSubStageLaborTotal(null);
            UpdateMaterialsTotal(null);
            return;
        }

        if (BuildingsGrid.SelectedItem is BuildingRow current && filtered.Contains(current))
        {
            return;
        }

        var first = filtered[0];
        _pendingStageSelection = null;
        BuildingsGrid.SelectedItem = first;
        BuildingsGrid.ScrollIntoView(first);
    }

    private void StatusFilterCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingStatusCheckBoxes)
        {
            return;
        }

        if (sender is not CheckBox checkBox || checkBox.Tag is not WorkStatus status)
        {
            return;
        }

        var wasShowingAll = _selectedStatusFilters.Count == _allWorkStatuses.Count;
        var allToggleIsChecked = StatusFilterAllCheckBox?.IsChecked == true;

        if (wasShowingAll && allToggleIsChecked)
        {
            _selectedStatusFilters = new HashSet<WorkStatus> { status };
            ApplyStatusFilter();
            return;
        }

        if (checkBox.IsChecked == true)
        {
            _selectedStatusFilters.Add(status);
        }
        else
        {
            _selectedStatusFilters.Remove(status);
        }

        ApplyStatusFilter();
    }

    private void StatusFilterAllCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingStatusCheckBoxes)
        {
            return;
        }

        if (StatusFilterAllCheckBox?.IsChecked == true)
        {
            _selectedStatusFilters = _allWorkStatuses.ToHashSet();
        }
        else
        {
            _selectedStatusFilters = new HashSet<WorkStatus>();
        }

        ApplyStatusFilter();
    }

    private void ApplyStatusFilter()
    {
        UpdateStatusFilterSummary();
        RefreshBuildingView();
    }

    private void UpdateStatusFilterSummary()
    {
        string label;

        if (_selectedStatusFilters.Count == 0)
        {
            label = ResourceHelper.GetString("OperationsView_StatusFilter_None", "No statuses selected");
        }
        else if (_selectedStatusFilters.Count == _allWorkStatuses.Count)
        {
            label = ResourceHelper.GetString("OperationsView_StatusFilter_All", "All statuses");
        }
        else
        {
            var names = _selectedStatusFilters
                .OrderBy(status => status)
                .Select(GetStatusDisplayName);

            label = string.Join(", ", names);
        }

        BuildingStatusFilterLabel = label;
        UpdateStatusFilterCheckBoxes();
    }

    private void UpdateStatusFilterCheckBoxes()
    {
        if (StatusFilterNotStartedCheckBox == null)
        {
            return;
        }

        _isUpdatingStatusCheckBoxes = true;

        try
        {
            var showAll = _selectedStatusFilters.Count == _allWorkStatuses.Count;

            if (StatusFilterAllCheckBox != null)
            {
                StatusFilterAllCheckBox.IsChecked = showAll ? true : false;
            }
            StatusFilterNotStartedCheckBox.IsChecked = showAll || (_selectedStatusFilters.Contains(WorkStatus.NotStarted) && _selectedStatusFilters.Count > 0);
            StatusFilterOngoingCheckBox.IsChecked = showAll || (_selectedStatusFilters.Contains(WorkStatus.Ongoing) && _selectedStatusFilters.Count > 0);
            StatusFilterFinishedCheckBox.IsChecked = showAll || (_selectedStatusFilters.Contains(WorkStatus.Finished) && _selectedStatusFilters.Count > 0);
            StatusFilterPaidCheckBox.IsChecked = showAll || (_selectedStatusFilters.Contains(WorkStatus.Paid) && _selectedStatusFilters.Count > 0);
            StatusFilterStoppedCheckBox.IsChecked = showAll || (_selectedStatusFilters.Contains(WorkStatus.Stopped) && _selectedStatusFilters.Count > 0);
        }
        finally
        {
            _isUpdatingStatusCheckBoxes = false;
        }
    }

    private string GetStatusDisplayName(WorkStatus status)
    {
        var converted = _statusToStringConverter.Convert(status, typeof(string), null, CultureInfo.CurrentUICulture);
        return converted?.ToString() ?? status.ToString();
    }

    private bool BuildingFilter(object? obj)
    {
        if (obj is not BuildingRow row)
        {
            return false;
        }

        if (_selectedStatusFilters.Count == 0)
        {
            return false;
        }

        if (_selectedStatusFilters.Count < _allWorkStatuses.Count && !_selectedStatusFilters.Contains(row.Status))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_buildingSearchText))
        {
            return true;
        }

        return row.Code?.IndexOf(_buildingSearchText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static decimal CompletionValue(WorkStatus status) =>
        status == WorkStatus.Finished || status == WorkStatus.Paid || status == WorkStatus.Stopped ? 1m : 0m;

    private static int ComputeBuildingProgress(Building b)
    {
        if (b.Stages == null || b.Stages.Count == 0)
            return (int)Math.Round(CompletionValue(b.Status) * 100m, MidpointRounding.AwayFromZero);

        decimal perStageWeight = 1m / b.Stages.Count;
        decimal sum = 0m;

        foreach (var s in b.Stages.OrderBy(s => s.OrderIndex))
        {
            decimal stageFraction;

            if (s.SubStages == null || s.SubStages.Count == 0)
            {
                stageFraction = CompletionValue(s.Status);
            }
            else
            {
                stageFraction = ComputeStageProgress(s) / 100m;
            }

            sum += perStageWeight * stageFraction;
        }

        return (int)Math.Round(sum * 100m, MidpointRounding.AwayFromZero);
    }

    private static int ComputeStageProgress(Stage stage)
    {
        if (stage.SubStages == null || stage.SubStages.Count == 0)
            return (int)Math.Round(CompletionValue(stage.Status) * 100m, MidpointRounding.AwayFromZero);

        var orderedSubs = stage.SubStages
            .OrderBy(ss => ss.OrderIndex)
            .ToList();

        if (orderedSubs.Count == 0)
            return (int)Math.Round(CompletionValue(stage.Status) * 100m, MidpointRounding.AwayFromZero);

        decimal perSub = 1m / orderedSubs.Count;
        decimal sum = 0m;

        foreach (var ss in orderedSubs)
            sum += perSub * CompletionValue(ss.Status);

        return (int)Math.Round(sum * 100m, MidpointRounding.AwayFromZero);
    }

    private static string ComputeCurrentSubStageName(Stage stage)
    {
        if (stage.Status == WorkStatus.Finished || stage.Status == WorkStatus.Paid || stage.Status == WorkStatus.Stopped)
            return string.Empty;

        if (stage.SubStages == null || stage.SubStages.Count == 0)
            return string.Empty;

        var ongoing = stage.SubStages
            .OrderBy(ss => ss.OrderIndex)
            .FirstOrDefault(ss => ss.Status == WorkStatus.Ongoing);

        return ongoing?.Name ?? string.Empty;
    }

    private static bool BuildingHasPaidItems(Building b)
    {
        if (b.Status == WorkStatus.Paid)
            return true;

        if (b.Stages == null || b.Stages.Count == 0)
            return false;

        foreach (var stage in b.Stages)
        {
            if (stage.Status == WorkStatus.Paid)
                return true;

            if (stage.SubStages == null)
                continue;

            if (stage.SubStages.Any(ss => ss.Status == WorkStatus.Paid))
                return true;
        }

        return false;
    }

    private static string ComputeCurrentStageName(Building b)
    {
        if (b.Stages == null || b.Stages.Count == 0)
            return string.Empty;

        // "Current" = first stage that is not fully done (i.e., not Finished/Paid/Stopped);
        // when all stages are done we return an empty string so the grid shows nothing.
        foreach (var s in b.Stages.OrderBy(s => s.OrderIndex))
        {
            bool stageDone = s.Status == WorkStatus.Finished || s.Status == WorkStatus.Paid || s.Status == WorkStatus.Stopped;

            if (s.SubStages != null && s.SubStages.Count > 0)
            {
                // If any sub-stage is not fully done, stage is current
                stageDone = s.SubStages.All(ss => ss.Status == WorkStatus.Finished || ss.Status == WorkStatus.Paid || ss.Status == WorkStatus.Stopped);
            }

            if (!stageDone)
                return s.Name;
        }

        return string.Empty;
    }

    private async Task SeedStagesForBuildingAsync(int buildingId, int buildingTypeId)
    {
        // Get ordered stage presets for the building type
        var links = await _db.BuildingTypeStagePresets
            .Where(x => x.BuildingTypeId == buildingTypeId)
            .OrderBy(x => x.OrderIndex)
            .Include(x => x.StagePreset)
            .ToListAsync();

        // Fetch all sub-stages for the involved presets in one go
        var presetIds = links.Select(l => l.StagePresetId).ToList();
        var subStagesByPreset = await _db.SubStagePresets
            .Where(s => presetIds.Contains(s.StagePresetId))
            .OrderBy(s => s.OrderIndex)
            .GroupBy(s => s.StagePresetId)
            .ToDictionaryAsync(g => g.Key, g => g.ToList());

        var laborLookup = await _db.BuildingTypeSubStageLabors
            .Where(x => x.BuildingTypeId == buildingTypeId)
            .ToDictionaryAsync(x => x.SubStagePresetId, x => x.LaborCost);

        int stageOrder = 1;

        foreach (var link in links)
        {
            var preset = link.StagePreset!;
            var newStage = new Stage
            {
                BuildingId = buildingId,
                Name = preset.Name,
                OrderIndex = stageOrder++,
                Status = WorkStatus.NotStarted,
                StartDate = null,
                EndDate = null,
                Notes = ""
            };
            _db.Stages.Add(newStage);
            await _db.SaveChangesAsync(); // need Id for sub-stages

            // Sub-stages for this preset (if any)
            if (subStagesByPreset.TryGetValue(preset.Id, out var subs))
            {
                int subOrder = 1;
                foreach (var ssp in subs)
                {
                    var newSub = new SubStage
                    {
                        StageId = newStage.Id,
                        Name = ssp.Name,
                        OrderIndex = subOrder++,
                        Status = WorkStatus.NotStarted,
                        StartDate = null,
                        EndDate = null,
                        LaborCost = laborLookup.TryGetValue(ssp.Id, out var labor) && labor.HasValue
                            ? labor.Value
                            : 0m
                    };
                    _db.SubStages.Add(newSub);
                }
                await _db.SaveChangesAsync();
            }
        }
    }

    private async Task ReloadBuildingsForCurrentProjectAsync(int? selectBuildingId = null)
    {
        if (_db == null || _currentProject == null) return;

        // your existing loader (projected columns + current stage name + progress)
        await ShowProject(_currentProject);

        if (selectBuildingId.HasValue)
        {
            var list = BuildingsGrid.ItemsSource as System.Collections.IEnumerable;
            if (list != null)
            {
                foreach (var item in list)
                {
                    var prop = item.GetType().GetProperty("Id");
                    if (prop != null && (int)prop.GetValue(item)! == selectBuildingId.Value)
                    {
                        BuildingsGrid.SelectedItem = item;
                        BuildingsGrid.ScrollIntoView(item);
                        break;
                    }
                }
            }
        }
    }

    private async void StopBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null || _currentProject == null) return;
        // Find selected building (from the anonymous row)
        var row = BuildingsGrid.SelectedItem as dynamic;
        if (row == null) return;
        int buildingId = row.Id;

        var confirm = MessageBox.Show(
            ResourceHelper.GetString("OperationsView_StopBuildingConfirmMessage", "Stop this building? All unfinished/unpaid stages and sub-stages will be marked Stopped."),
            ResourceHelper.GetString("OperationsView_StopBuildingConfirmTitle", "Confirm stop"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var b = await _db.Buildings
                .Include(b => b.Project)
                .Include(b => b.Stages)
                    .ThenInclude(s => s.SubStages)
                        .ThenInclude(ss => ss.MaterialUsages)
                            .ThenInclude(mu => mu.Material)
                                .ThenInclude(m => m.PriceHistory)
                .FirstAsync(b => b.Id == buildingId);

            b.Status = WorkStatus.Stopped;

            var compensationEntries = new List<CompensationEntry>();

            foreach (var s in b.Stages.OrderBy(stage => stage.OrderIndex))
            {
                if (s.Status != WorkStatus.Finished && s.Status != WorkStatus.Paid)
                    s.Status = WorkStatus.Stopped;

                foreach (var ss in s.SubStages.OrderBy(subStage => subStage.OrderIndex))
                {
                    if (ss.Status != WorkStatus.Finished && ss.Status != WorkStatus.Paid)
                    {
                        compensationEntries.Add(new CompensationEntry
                        {
                            Stage = s,
                            SubStage = ss,
                            StageOrder = s.OrderIndex,
                            SubStageOrder = ss.OrderIndex
                        });
                        ss.Status = WorkStatus.Stopped;
                    }
                }
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await ExportCompensationAsync(b, compensationEntries);

            // Refresh UI (keep selection)
            await ReloadBuildingsAsync(buildingId);
            await ReloadStagesAndSubStagesAsync(buildingId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_StopBuildingFailedFormat", "Failed to stop building:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void DeleteBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null || _currentProject == null) return;

        if (sender is not Button btn) return;

        if (btn.Tag is not int buildingId) return;

        var building = await _db.Buildings
            .Include(b => b.Stages)
                .ThenInclude(s => s.SubStages)
            .FirstOrDefaultAsync(b => b.Id == buildingId);

        if (building == null) return;

        if (BuildingHasPaidItems(building))
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_DeleteBuildingPaidMessage", "This building has paid items and cannot be deleted."),
                ResourceHelper.GetString("OperationsView_DeleteBuildingTitle", "Delete building"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            string.Format(ResourceHelper.GetString("OperationsView_DeleteBuildingConfirmFormat", "Delete building '{0}'? All stages and sub-stages will be removed."), building.Code),
            ResourceHelper.GetString("OperationsView_DeleteBuildingTitle", "Delete building"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            _db.Buildings.Remove(building);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_DeleteBuildingFailedFormat", "Failed to delete building:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (_currentBuildingId == buildingId)
        {
            _currentBuildingId = null;
            _currentStageId = null;
            _pendingStageSelection = null;
            StagesGrid.ItemsSource = null;
            SubStagesGrid.ItemsSource = null;
            MaterialsGrid.ItemsSource = null;
            UpdateSubStageLaborTotal(null);
            UpdateMaterialsTotal(null);
            Breadcrumb = _currentProject.Name;
        }

        await ReloadBuildingsAsync();
    }

    private async void StartSubStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        // assuming SubStagesGrid binds to SubStage entities (not anonymous)
        var sel = SubStagesGrid.SelectedItem as SubStage;
        if (sel == null) return;

        // Load full entity + its stage and building
        var ss = await _db.SubStages
            .Include(x => x.Stage)
                .ThenInclude(s => s.Building)
            .FirstAsync(x => x.Id == sel.Id);

        // Rule: only one Ongoing sub-stage in the building
        bool hasOngoingElsewhere = await _db.SubStages
            .AnyAsync(x => x.Stage.BuildingId == ss.Stage.BuildingId && x.Status == WorkStatus.Ongoing && x.Id != ss.Id);
        if (hasOngoingElsewhere)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SubStageAlreadyInProgress", "Another sub-stage is already in progress in this building. Finish it first."),
                ResourceHelper.GetString("OperationsView_RuleTitle", "Rule"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // ❗ Order rule #1: previous sub-stage (same stage) must be Finished or Paid
        if (ss.OrderIndex > 1)
        {
            var prevSub = await _db.SubStages
                .Where(x => x.StageId == ss.StageId && x.OrderIndex == ss.OrderIndex - 1)
                .Select(x => new { x.Id, x.Status })
                .FirstOrDefaultAsync();

            if (prevSub == null || (prevSub.Status != WorkStatus.Finished && prevSub.Status != WorkStatus.Paid))
            {
                MessageBox.Show(
                    ResourceHelper.GetString("OperationsView_PreviousSubStageRequired", "You must finish the previous sub-stage first."),
                    ResourceHelper.GetString("OperationsView_RuleOrderTitle", "Order rule"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        // ❗ Order rule #2: all earlier stages must be Finished or Paid
        if (ss.Stage.OrderIndex > 1)
        {
            bool allPrevStagesDone = await _db.Stages
                .Where(s => s.BuildingId == ss.Stage.BuildingId && s.OrderIndex < ss.Stage.OrderIndex)
                .AllAsync(s => s.Status == WorkStatus.Finished || s.Status == WorkStatus.Paid);

            if (!allPrevStagesDone)
            {
                MessageBox.Show(
                    ResourceHelper.GetString("OperationsView_PreviousStagesRequired", "You must finish all previous stages before starting this stage."),
                    ResourceHelper.GetString("OperationsView_RuleOrderTitle", "Order rule"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        // State checks
        if (ss.Status == WorkStatus.Finished || ss.Status == WorkStatus.Paid || ss.Status == WorkStatus.Stopped)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SubStageCannotStart", "This sub-stage cannot be started in its current state."),
                ResourceHelper.GetString("OperationsView_RuleTitle", "Rule"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // ✅ Start sub-stage
        ss.Status = WorkStatus.Ongoing;
        if (ss.StartDate == null) ss.StartDate = DateTime.Today;

        // Bubble up to stage/building
        if (ss.Stage.Status == WorkStatus.NotStarted)
        {
            ss.Stage.Status = WorkStatus.Ongoing;
            if (ss.Stage.StartDate == null) ss.Stage.StartDate = DateTime.Today;
        }
        if (ss.Stage.Building.Status == WorkStatus.NotStarted)
        {
            ss.Stage.Building.Status = WorkStatus.Ongoing;
        }

        await _db.SaveChangesAsync();

        await ReloadBuildingsAsync(ss.Stage.BuildingId, ss.StageId);
        await ReloadStagesAndSubStagesAsync(ss.Stage.BuildingId, ss.StageId);
    }

    private async void FinishSubStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        var sel = SubStagesGrid.SelectedItem as SubStage;
        if (sel == null) return;

        var ss = await _db.SubStages
            .Include(x => x.MaterialUsages)
            .Include(x => x.Stage)
                .ThenInclude(s => s.SubStages)
            .Include(x => x.Stage.Building)
                .ThenInclude(b => b.Stages)
            .FirstAsync(x => x.Id == sel.Id);

        // ✅ Only Ongoing can be finished
        if (ss.Status != WorkStatus.Ongoing)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_OnlyOngoingCanFinish", "You can only finish an Ongoing sub-stage."),
                ResourceHelper.GetString("OperationsView_RuleTitle", "Rule"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ss.Status = WorkStatus.Finished;
        if (ss.EndDate == null) ss.EndDate = DateTime.Today;

        var freezeDate = ss.EndDate.Value.Date;
        foreach (var usage in ss.MaterialUsages)
        {
            usage.UsageDate = freezeDate;
        }

        // Recompute parents (also sets dates)
        UpdateStageStatusFromSubStages(ss.Stage);
        UpdateBuildingStatusFromStages(ss.Stage.Building);

        await _db.SaveChangesAsync();

        await ReloadBuildingsAsync(ss.Stage.BuildingId, ss.StageId);
        await ReloadStagesAndSubStagesAsync(ss.Stage.BuildingId, ss.StageId);
    }

    private async void ResetSubStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        var sel = SubStagesGrid.SelectedItem as SubStage;
        if (sel == null) return;

        var ss = await _db.SubStages
            .Include(x => x.Stage)
                .ThenInclude(s => s.SubStages)
            .Include(x => x.Stage.Building)
                .ThenInclude(b => b.Stages)
            .FirstAsync(x => x.Id == sel.Id);

        if (ss.Status != WorkStatus.Ongoing)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_OnlyOngoingCanReset", "Only an Ongoing sub-stage can be reset to Not Started."),
                ResourceHelper.GetString("OperationsView_RuleTitle", "Rule"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ss.Status = WorkStatus.NotStarted;
        ss.StartDate = null;
        ss.EndDate = null;

        UpdateStageStatusFromSubStages(ss.Stage);
        UpdateBuildingStatusFromStages(ss.Stage.Building);

        await _db.SaveChangesAsync();

        await ReloadBuildingsAsync(ss.Stage.BuildingId, ss.StageId);
        await ReloadStagesAndSubStagesAsync(ss.Stage.BuildingId, ss.StageId);
    }

    private async Task ReloadStagesAndSubStagesAsync(int buildingId, int? preferredStageId = null)
    {
        // Reload stages for buildingId
        var stageEntities = await _db.Stages
            .Where(s => s.BuildingId == buildingId)
            .OrderBy(s => s.OrderIndex)
            .Include(s => s.SubStages)
            .AsNoTracking()
            .ToListAsync();

        var stageRows = stageEntities
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Status,
                ProgressPercent = ComputeStageProgress(s),
                OngoingSubStageName = ComputeCurrentSubStageName(s),
                PaidDate = s.Status == WorkStatus.Paid ? s.EndDate : null
            })
            .ToList();

        StagesGrid.ItemsSource = stageRows;

        // Select preferred stage (or first)
        int stageId = preferredStageId ?? stageRows.FirstOrDefault()?.Id ?? 0;

        if (stageId == 0)
        {
            StagesGrid.SelectedItem = null;
            SubStagesGrid.ItemsSource = null;
            MaterialsGrid.ItemsSource = null;
            _currentStageId = null;
            UpdateSubStageLaborTotal(null);
            UpdateMaterialsTotal(null);
            return;
        }

        var stageToSelect = stageRows.FirstOrDefault(s => s.Id == stageId);
        if (stageToSelect != null)
        {
            StagesGrid.SelectedItem = stageToSelect;
            StagesGrid.ScrollIntoView(stageToSelect);
        }
        _currentStageId = stageId;

        // Load tracked sub-stages for that stage
        var subs = await _db.SubStages
            .Where(ss => ss.StageId == stageId)
            .OrderBy(ss => ss.OrderIndex)
            .ToListAsync();

        SubStagesGrid.ItemsSource = subs;
        UpdateSubStageLaborTotal(subs);

        // Auto-select first sub-stage (if any) and load its materials
        var firstSub = subs.FirstOrDefault();
        if (firstSub != null)
        {
            SubStagesGrid.SelectedItem = firstSub;
            SubStagesGrid.ScrollIntoView(firstSub);

            await LoadMaterialsForSubStageAsync(firstSub);
        }
        else
        {
            MaterialsGrid.ItemsSource = null;
            UpdateMaterialsTotal(null);
        }
    }

    private static void UpdateStageStatusFromSubStages(Stage? s)
    {
        if (s == null)
        {
            return;
        }

        if (s.SubStages == null || s.SubStages.Count == 0)
        {
            s.Status = WorkStatus.NotStarted;
            s.StartDate = null;
            s.EndDate = null;
            return;
        }

        bool allPaid = s.SubStages.All(ss => ss.Status == WorkStatus.Paid);
        bool allStopped = s.SubStages.All(ss => ss.Status == WorkStatus.Stopped);
        bool anyStopped = s.SubStages.Any(ss => ss.Status == WorkStatus.Stopped);
        bool anyOngoing = s.SubStages.Any(ss => ss.Status == WorkStatus.Ongoing);
        bool allNotStarted = s.SubStages.All(ss => ss.Status == WorkStatus.NotStarted);
        bool allFinishedOrPaid = s.SubStages.All(ss => ss.Status == WorkStatus.Finished || ss.Status == WorkStatus.Paid);
        bool anyFinishedOrPaid = s.SubStages.Any(ss => ss.Status == WorkStatus.Finished || ss.Status == WorkStatus.Paid);

        if (allStopped)
        {
            s.Status = WorkStatus.Stopped;
        }
        else if (allPaid)
        {
            s.Status = WorkStatus.Paid;
        }
        else if (allFinishedOrPaid)
        {
            s.Status = WorkStatus.Finished;
        }
        else if (anyOngoing)
        {
            s.Status = WorkStatus.Ongoing;
        }
        else if (anyStopped)
        {
            s.Status = WorkStatus.Stopped;
        }
        else if (allNotStarted)
        {
            s.Status = WorkStatus.NotStarted;
        }
        else if (anyFinishedOrPaid)
        {
            s.Status = WorkStatus.Ongoing;
        }
        else
        {
            s.Status = WorkStatus.Ongoing;
        }

        if (s.Status == WorkStatus.Ongoing && s.StartDate == null)
            s.StartDate = DateTime.Today;

        if ((s.Status == WorkStatus.Finished || s.Status == WorkStatus.Paid) && s.EndDate == null)
            s.EndDate = DateTime.Today;

        if (s.Status == WorkStatus.Stopped)
        {
            if (s.StartDate == null)
                s.StartDate = DateTime.Today;
            if (s.EndDate == null)
                s.EndDate = DateTime.Today;
        }
        else if (s.Status == WorkStatus.NotStarted)
        {
            s.StartDate = null;
            s.EndDate = null;
        }
    }

    private static void UpdateBuildingStatusFromStages(Building? b)
    {
        if (b == null || b.Stages == null || b.Stages.Count == 0) return;

        bool anyStopped = b.Stages.Any(st => st.Status == WorkStatus.Stopped);
        bool allPaid = b.Stages.All(st => st.Status == WorkStatus.Paid);
        bool allFinishedOrPaid = b.Stages.All(st => st.Status == WorkStatus.Finished || st.Status == WorkStatus.Paid);
        bool allNotStarted = b.Stages.All(st => st.Status == WorkStatus.NotStarted);
        bool anyOngoing = b.Stages.Any(st => st.Status == WorkStatus.Ongoing);
        bool anyFinishedOrPaid = b.Stages.Any(st => st.Status == WorkStatus.Finished || st.Status == WorkStatus.Paid);

        if (anyStopped)
        {
            b.Status = WorkStatus.Stopped;
        }
        else if (allPaid)
        {
            b.Status = WorkStatus.Paid;
        }
        else if (allFinishedOrPaid)
        {
            b.Status = WorkStatus.Finished;
        }
        else if (allNotStarted)
        {
            b.Status = WorkStatus.NotStarted;
        }
        else if (anyOngoing)
        {
            b.Status = WorkStatus.Ongoing;
        }
        else if (anyFinishedOrPaid)
        {
            b.Status = WorkStatus.Ongoing;
        }
        else
        {
            b.Status = WorkStatus.Ongoing;
        }
    }

    private async void AddMaterialToSubStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null)
            return;

        // We require a selected SubStage (you’re already binding real SubStage entities)
        var ss = SubStagesGrid.SelectedItem as SubStage;
        if (ss == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectSubStageFirst", "Select a sub-stage first."),
                ResourceHelper.GetString("OperationsView_NoSubStageTitle", "No sub-stage"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Open your dialog (filters active materials not already used by this sub-stage)
        var dlg = new Kanstraction.Views.AddMaterialToSubStageDialog(_db, ss.Id)
        {
            Owner = Window.GetWindow(this)
        };

        if (dlg.ShowDialog() != true)
            return;

        // Defensive checks (dialog already validates)
        if (dlg.MaterialId == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectMaterialPrompt", "Please select a material."),
                ResourceHelper.GetString("Common_RequiredTitle", "Required"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Create a MaterialUsage row (instance-only, does not touch presets)
        var mu = new MaterialUsage
        {
            SubStageId = ss.Id,
            MaterialId = dlg.MaterialId.Value,
            Qty = dlg.Qty,
            UsageDate = DateTime.Today,
            Notes = null
        };

        try
        {
            _db.MaterialUsages.Add(mu);
            await _db.SaveChangesAsync();

            // Refresh the materials list for this sub-stage
            await LoadMaterialsForSubStageAsync(ss);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_AddMaterialFailedFormat", "Failed to add material:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void DeleteMaterialUsage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null)
            return;

        if (sender is not Button btn)
            return;

        if (btn.Tag is not int usageId)
            return;

        var usage = await _db.MaterialUsages
            .Include(mu => mu.Material)
            .Include(mu => mu.SubStage)
            .FirstOrDefaultAsync(mu => mu.Id == usageId);

        if (usage == null)
            return;

        var confirm = MessageBox.Show(
            string.Format(ResourceHelper.GetString("OperationsView_DeleteMaterialConfirmFormat", "Delete material '{0}'?"), usage.Material.Name),
            ResourceHelper.GetString("OperationsView_DeleteMaterialTitle", "Delete material"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            _db.MaterialUsages.Remove(usage);
            await _db.SaveChangesAsync();

            _editingMaterialUsage = null;
            _originalMaterialQuantity = null;

            if (usage.SubStage != null)
            {
                await LoadMaterialsForSubStageAsync(usage.SubStage);
            }
            else
            {
                var subStage = await _db.SubStages.FirstOrDefaultAsync(ss => ss.Id == usage.SubStageId);
                if (subStage != null)
                {
                    await LoadMaterialsForSubStageAsync(subStage);
                }
                else
                {
                    MaterialsGrid.ItemsSource = null;
                    UpdateMaterialsTotal(null);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_DeleteMaterialFailedFormat", "Failed to delete material:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task GenerateProgressReportAsync(DateTime startDate, DateTime endDate)
    {
        if (_db == null || _currentProject == null)
        {
            return;
        }

        var buildingTypeIds = await _db.Buildings
            .Where(b => b.ProjectId == _currentProject.Id && b.BuildingTypeId != null)
            .Select(b => b.BuildingTypeId!.Value)
            .Distinct()
            .ToListAsync();

        if (buildingTypeIds.Count == 0)
        {
            MessageBox.Show(
                ResourceHelper.GetString("ProgressReport_NoBuildingTypesMessage", "This project has no buildings with a building type. Nothing to export."),
                ResourceHelper.GetString("ProgressReport_NoBuildingTypesTitle", "No building types"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var buildingTypes = await _db.BuildingTypes
            .Where(bt => buildingTypeIds.Contains(bt.Id))
            .Select(bt => new { bt.Id, bt.Name })
            .OrderBy(bt => bt.Name)
            .ToListAsync();

        var assignments = await _db.BuildingTypeStagePresets
            .Where(x => buildingTypeIds.Contains(x.BuildingTypeId))
            .Select(x => new
            {
                x.BuildingTypeId,
                x.StagePresetId,
                x.OrderIndex,
                StageName = x.StagePreset.Name
            })
            .ToListAsync();

        var stagePresetIds = assignments.Select(x => x.StagePresetId).Distinct().ToList();

        var subStagePresets = await _db.SubStagePresets
            .Where(ss => stagePresetIds.Contains(ss.StagePresetId))
            .Select(ss => new
            {
                ss.Id,
                ss.StagePresetId,
                ss.Name,
                ss.OrderIndex
            })
            .ToListAsync();

        var includeRows = await _db.BuildingTypeSubStageLabors
            .Where(x => buildingTypeIds.Contains(x.BuildingTypeId))
            .Select(x => new { x.BuildingTypeId, x.SubStagePresetId, x.IncludeInReport })
            .ToListAsync();

        var includeLookup = includeRows.ToDictionary(x => (x.BuildingTypeId, x.SubStagePresetId), x => x.IncludeInReport);

        var lastSubStageIds = subStagePresets
            .GroupBy(x => x.StagePresetId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.OrderIndex).LastOrDefault()?.Id);

        var stageAssignmentsByType = assignments
            .GroupBy(x => x.BuildingTypeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.OrderIndex)
                      .Select((x, index) => new StageAssignmentInfo
                      {
                          StagePresetId = x.StagePresetId,
                          StageName = x.StageName ?? string.Empty,
                          Position = index + 1
                      })
                      .ToList());

        var buildings = await _db.Buildings
            .Where(b => b.ProjectId == _currentProject.Id && b.BuildingTypeId != null)
            .Include(b => b.Stages)
                .ThenInclude(s => s.SubStages)
            .AsNoTracking()
            .ToListAsync();

        var reports = new List<ProgressReportData>();
        bool anyBuildingIncluded = false;

        foreach (var type in buildingTypes)
        {
            var report = new ProgressReportData
            {
                TypeName = type.Name ?? string.Empty
            };

            if (stageAssignmentsByType.TryGetValue(type.Id, out var stageInfos))
            {
                foreach (var stageInfo in stageInfos)
                {
                    var subPresetsForStage = subStagePresets
                        .Where(ss => ss.StagePresetId == stageInfo.StagePresetId)
                        .OrderBy(ss => ss.OrderIndex)
                        .Select((ss, index) => new { Preset = ss, Position = index + 1 })
                        .ToList();

                    foreach (var entry in subPresetsForStage)
                    {
                        var preset = entry.Preset;
                        bool include = includeLookup.TryGetValue((type.Id, preset.Id), out var includeFlag)
                            ? includeFlag
                            : (lastSubStageIds.TryGetValue(stageInfo.StagePresetId, out var lastId) && lastId == preset.Id);

                        if (!include)
                        {
                            continue;
                        }

                        report.Columns.Add(new ReportColumn
                        {
                            StagePresetId = stageInfo.StagePresetId,
                            StageName = stageInfo.StageName,
                            StagePosition = stageInfo.Position,
                            SubStagePresetId = preset.Id,
                            SubStageName = preset.Name,
                            SubStageOrderIndex = preset.OrderIndex,
                            SubStagePosition = entry.Position
                        });
                    }
                }
            }

            var buildingsForType = buildings
                .Where(b => b.BuildingTypeId == type.Id)
                .OrderBy(b => b.Code, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var filtered = new List<Building>();
            foreach (var building in buildingsForType)
            {
                if (ShouldSkipBuilding(building, startDate, endDate))
                {
                    continue;
                }

                filtered.Add(building);
            }

            if (filtered.Count > 0)
            {
                anyBuildingIncluded = true;
            }

            report.Buildings.Clear();
            report.Buildings.AddRange(filtered);
            reports.Add(report);
        }

        if (!anyBuildingIncluded)
        {
            MessageBox.Show(
                ResourceHelper.GetString("ProgressReport_NoEligibleBuildingsMessage", "No buildings match the selected duration."),
                ResourceHelper.GetString("ProgressReport_NoEligibleBuildingsTitle", "No buildings"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var sanitizedProjectName = SanitizeFileName(_currentProject?.Name, "Projet");
        var formattedDate = DateTime.Now.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

        var sfd = new SaveFileDialog
        {
            Title = ResourceHelper.GetString("ProgressReport_SaveDialogTitle", "Export work progress report"),
            Filter = ResourceHelper.GetString("ProgressReport_SaveDialogFilter", "Excel files (*.xlsx)|*.xlsx"),
            FileName = $"RAPPORT AVANCEMENT DE TRAVAUX {sanitizedProjectName} {formattedDate}.xlsx"
        };

        var owner = Window.GetWindow(this);
        if (sfd.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            using var workbook = new XLWorkbook();
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var buildingHeader = ResourceHelper.GetString("ProgressReport_BuildingCodeHeader", "Lot");
            var stoppedLabel = ResourceHelper.GetString("WorkStatus_Stopped", "Stopped");

            var projectName = _currentProject?.Name ?? string.Empty;

            foreach (var report in reports)
            {
                var sheetName = SanitizeWorksheetName(report.TypeName, existingNames);
                var ws = workbook.Worksheets.Add(sheetName);
                WriteProgressWorksheet(ws, report, buildingHeader, stoppedLabel, projectName);
            }

            workbook.SaveAs(sfd.FileName);
            TryOpenExportedFile(sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("ProgressReport_ExportFailedFormat", "Failed to export the progress report:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task GenerateRemainingCostReportAsync(RemainingCostReportType reportType)
    {
        if (_db == null || _currentProject == null)
        {
            return;
        }

        var buildingTypeIds = await _db.Buildings
            .Where(b => b.ProjectId == _currentProject.Id && b.BuildingTypeId != null)
            .Select(b => b.BuildingTypeId!.Value)
            .Distinct()
            .ToListAsync();

        if (buildingTypeIds.Count == 0)
        {
            MessageBox.Show(
                ResourceHelper.GetString("RemainingCostReport_NoBuildingTypesMessage", "This project has no buildings with a building type. Nothing to export."),
                ResourceHelper.GetString("RemainingCostReport_NoBuildingTypesTitle", "No building types"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var buildingTypes = await _db.BuildingTypes
            .Where(bt => buildingTypeIds.Contains(bt.Id))
            .Select(bt => new { bt.Id, bt.Name })
            .OrderBy(bt => bt.Name)
            .ToListAsync();

        var stageAssignments = await _db.BuildingTypeStagePresets
            .Where(x => buildingTypeIds.Contains(x.BuildingTypeId))
            .Select(x => new
            {
                x.BuildingTypeId,
                x.OrderIndex,
                StageName = x.StagePreset.Name
            })
            .ToListAsync();

        var stageLabel = ResourceHelper.GetString("OperationsView_StageLabel", "Stage");
        var columnsByType = stageAssignments
            .GroupBy(x => x.BuildingTypeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.OrderIndex)
                      .Select(x => new RemainingStageColumn
                      {
                          Name = string.IsNullOrWhiteSpace(x.StageName)
                              ? $"{stageLabel} {x.OrderIndex}"
                              : x.StageName,
                          OrderIndex = x.OrderIndex
                      })
                      .ToList());

        var buildings = await _db.Buildings
            .Where(b => b.ProjectId == _currentProject.Id && b.BuildingTypeId != null)
            .Include(b => b.Stages)
                .ThenInclude(s => s.SubStages)
                    .ThenInclude(ss => ss.MaterialUsages)
                        .ThenInclude(mu => mu.Material)
                            .ThenInclude(m => m.PriceHistory)
            .AsNoTracking()
            .OrderBy(b => b.Code)
            .ToListAsync();

        if (buildings.Count == 0)
        {
            MessageBox.Show(
                ResourceHelper.GetString("RemainingCostReport_NoBuildingsMessage", "There are no buildings to export."),
                ResourceHelper.GetString("RemainingCostReport_NoBuildingsTitle", "No buildings"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var reportTitleKey = reportType == RemainingCostReportType.Labor
            ? "RemainingCostReport_TitleLaborSuffix"
            : "RemainingCostReport_TitleMaterialSuffix";
        var reportTitleSuffix = ResourceHelper.GetString(
            reportTitleKey,
            reportType == RemainingCostReportType.Labor ? "Main d'oeuvre" : "Matériaux");
        var projectName = _currentProject.Name ?? string.Empty;
        var reportTitle = string.IsNullOrWhiteSpace(projectName)
            ? reportTitleSuffix
            : $"{projectName} - {reportTitleSuffix}";

        var dialogTitle = reportType == RemainingCostReportType.Labor
            ? ResourceHelper.GetString("RemainingCostReport_SaveDialogTitleLabor", "Export remaining labor costs report")
            : ResourceHelper.GetString("RemainingCostReport_SaveDialogTitleMaterial", "Export remaining material costs report");
        var dialogFilter = ResourceHelper.GetString("RemainingCostReport_SaveDialogFilter", "Excel files (*.xlsx)|*.xlsx");
        var fileNameBase = SanitizeFileName(reportTitle, "Rapport");

        var sfd = new SaveFileDialog
        {
            Title = dialogTitle,
            Filter = dialogFilter,
            FileName = $"{fileNameBase}.xlsx"
        };

        var owner = Window.GetWindow(this);
        if (sfd.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            using var workbook = new XLWorkbook();
            var buildingHeader = ResourceHelper.GetString("ProgressReport_BuildingCodeHeader", "Lot");
            var totalHeader = ResourceHelper.GetString("RemainingCostReport_TotalHeader", "Total restant");
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var type in buildingTypes)
            {
                var typeBuildings = buildings
                    .Where(b => b.BuildingTypeId == type.Id)
                    .OrderBy(b => b.Code, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                if (typeBuildings.Count == 0)
                {
                    continue;
                }

                if (!columnsByType.TryGetValue(type.Id, out var stageColumns))
                {
                    stageColumns = new List<RemainingStageColumn>();
                }

                var report = new RemainingCostReportData
                {
                    ProjectName = _currentProject.Name,
                    Columns = stageColumns,
                    Buildings = typeBuildings
                };

                var sheetName = SanitizeWorksheetName(type.Name, existingNames);
                var ws = workbook.Worksheets.Add(sheetName);
                WriteRemainingCostWorksheet(ws, report, buildingHeader, totalHeader, reportTitle, reportType);
            }

            workbook.SaveAs(sfd.FileName);
            TryOpenExportedFile(sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("RemainingCostReport_ExportFailedFormat", "Failed to export the remaining costs report:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ExportSubStages_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null)
        {
            return;
        }

        int? stageId = _currentStageId;
        if (!stageId.HasValue)
        {
            if (StagesGrid.SelectedItem is Stage stageEntity)
            {
                stageId = stageEntity.Id;
            }
            else if (StagesGrid.SelectedItem != null)
            {
                try
                {
                    stageId = (int)((dynamic)StagesGrid.SelectedItem).Id;
                }
                catch
                {
                    stageId = null;
                }
            }
        }

        if (!stageId.HasValue)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectStageFirst", "Select a stage first."),
                ResourceHelper.GetString("OperationsView_NoStageTitle", "No stage"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var stage = await _db.Stages
            .Include(s => s.Building)
                .ThenInclude(b => b.Project)
            .FirstOrDefaultAsync(s => s.Id == stageId.Value);

        if (stage == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectStageFirst", "Select a stage first."),
                ResourceHelper.GetString("OperationsView_NoStageTitle", "No stage"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var subStages = await _db.SubStages
            .Where(ss => ss.StageId == stage.Id)
            .OrderBy(ss => ss.OrderIndex)
            .ToListAsync();

        if (subStages.Count == 0)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_NoSubStagesToExport", "There are no sub-stages to export."),
                ResourceHelper.GetString("OperationsView_NoSubStageTitle", "No sub-stage"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var projectLabel = ResourceHelper.GetString("OperationsView_ProjectLabel", "Project");
        var lotLabel = ResourceHelper.GetString("OperationsView_LotLabel", "Lot");
        var stageLabel = ResourceHelper.GetString("OperationsView_StageLabel", "Stage");

        var buildingCode = stage.Building?.Code ?? string.Empty;
        var projectName = stage.Building?.Project?.Name ?? _currentProject?.Name ?? string.Empty;

        var fileNameBase = $"{SanitizeFileName(buildingCode, lotLabel)}_{SanitizeFileName(stage.Name, stageLabel)}_{DateTime.Now:yyyyMMdd_HHmm}";
        var sfd = new SaveFileDialog
        {
            Title = ResourceHelper.GetString("OperationsView_ExportSubStagesTitle", "Export sub-stages"),
            Filter = ResourceHelper.GetString("OperationsView_ExportSubStagesFilter", "Excel files (*.xlsx)|*.xlsx"),
            FileName = $"{fileNameBase}.xlsx"
        };

        var owner = Window.GetWindow(this);
        if (sfd.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            using var workbook = new XLWorkbook();
            var worksheetName = ResourceHelper.GetString("OperationsView_SubStages", "Sub-stages");
            if (string.IsNullOrWhiteSpace(worksheetName))
            {
                worksheetName = "Sub-stages";
            }

            var ws = workbook.AddWorksheet(worksheetName);

            WriteInfoRow(ws, 1, projectLabel, projectName);
            WriteInfoRow(ws, 2, lotLabel, buildingCode);
            WriteInfoRow(ws, 3, stageLabel, stage.Name ?? string.Empty);

            var nameHeader = ResourceHelper.GetString("OperationsView_Name", "Name");
            var statusHeader = ResourceHelper.GetString("OperationsView_Status", "Status");
            var laborHeader = ResourceHelper.GetString("OperationsView_Labor", "Labor");

            ws.Cell(5, 1).Value = nameHeader;
            ws.Cell(5, 2).Value = statusHeader;
            ws.Cell(5, 3).Value = laborHeader;
            var subStageHeader = ws.Range(5, 1, 5, 3);
            subStageHeader.Style.Font.Bold = true;
            subStageHeader.Style.Font.FontColor = SubStageHeaderFontColor;
            subStageHeader.Style.Fill.BackgroundColor = SubStageHeaderFillColor;
            subStageHeader.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            subStageHeader.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(5).Height = 20;

            int row = 6;
            foreach (var ss in subStages)
            {
                ws.Cell(row, 1).Value = ss.Name;
                var statusKey = $"WorkStatus_{ss.Status}";
                ws.Cell(row, 2).Value = ResourceHelper.GetString(statusKey, ss.Status.ToString());
                ws.Cell(row, 3).Value = ss.LaborCost;
                row++;
            }

            ws.Column(3).Style.NumberFormat.Format = "#,##0.00";
            ApplyAlternatingRowStyles(ws, 6, row - 1, 1, 3, SubStageRowPrimaryFill, SubStageRowSecondaryFill);
            ws.Columns(1, 3).AdjustToContents();

            workbook.SaveAs(sfd.FileName);
            TryOpenExportedFile(sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_ExportFailedFormat", "Failed to export sub-stages:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ExportSubStagesMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private async void ExportMaterials_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null)
        {
            return;
        }

        int? subStageId = null;
        if (SubStagesGrid.SelectedItem is SubStage selectedSubStage)
        {
            subStageId = selectedSubStage.Id;
        }
        else if (SubStagesGrid.SelectedItem != null)
        {
            try
            {
                subStageId = (int)((dynamic)SubStagesGrid.SelectedItem).Id;
            }
            catch
            {
                subStageId = null;
            }
        }

        if (!subStageId.HasValue)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectSubStageFirst", "Select a sub-stage first."),
                ResourceHelper.GetString("OperationsView_NoSubStageTitle", "No sub-stage"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var subStage = await _db.SubStages
            .Include(ss => ss.Stage)
                .ThenInclude(st => st.Building)
                    .ThenInclude(b => b.Project)
            .FirstOrDefaultAsync(ss => ss.Id == subStageId.Value);

        if (subStage == null || subStage.Stage == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectSubStageFirst", "Select a sub-stage first."),
                ResourceHelper.GetString("OperationsView_NoSubStageTitle", "No sub-stage"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var usages = await _db.MaterialUsages
            .Include(mu => mu.Material)
                .ThenInclude(m => m.PriceHistory)
            .Include(mu => mu.Material)
                .ThenInclude(m => m.MaterialCategory)
            .Where(mu => mu.SubStageId == subStage.Id)
            .OrderBy(mu => mu.Material.Name)
            .ToListAsync();

        if (usages.Count == 0)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_NoMaterialsToExport", "There are no materials to export."),
                ResourceHelper.GetString("OperationsView_NoMaterialsTitle", "No materials"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        bool freezePrices = subStage.Status == WorkStatus.Finished || subStage.Status == WorkStatus.Paid;

        var projectLabel = ResourceHelper.GetString("OperationsView_ProjectLabel", "Project");
        var lotLabel = ResourceHelper.GetString("OperationsView_LotLabel", "Lot");
        var stageLabel = ResourceHelper.GetString("OperationsView_StageLabel", "Stage");
        var subStageLabel = ResourceHelper.GetString("OperationsView_SubStageLabel", "Sub-stage");

        var stageName = subStage.Stage.Name ?? string.Empty;
        var subStageName = subStage.Name ?? string.Empty;
        var buildingCode = subStage.Stage.Building?.Code ?? string.Empty;
        var projectName = subStage.Stage.Building?.Project?.Name ?? _currentProject?.Name ?? string.Empty;

        var fileNameBase = $"{SanitizeFileName(buildingCode, lotLabel)}_{SanitizeFileName(stageName, stageLabel)}_{SanitizeFileName(subStageName, subStageLabel)}_{DateTime.Now:yyyyMMdd_HHmm}";

        var sfd = new SaveFileDialog
        {
            Title = ResourceHelper.GetString("OperationsView_ExportMaterialsTitle", "Export materials"),
            Filter = ResourceHelper.GetString("OperationsView_ExportMaterialsFilter", "Excel files (*.xlsx)|*.xlsx"),
            FileName = $"{fileNameBase}.xlsx"
        };

        var owner = Window.GetWindow(this);
        if (sfd.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            using var workbook = new XLWorkbook();
            var worksheetName = ResourceHelper.GetString("OperationsView_Materials", "Materials");
            if (string.IsNullOrWhiteSpace(worksheetName))
            {
                worksheetName = "Materials";
            }

            var ws = workbook.AddWorksheet(worksheetName);

            WriteInfoRow(ws, 1, projectLabel, projectName);
            WriteInfoRow(ws, 2, lotLabel, buildingCode);
            WriteInfoRow(ws, 3, stageLabel, stageName);
            WriteInfoRow(ws, 4, subStageLabel, subStageName);

            var materialHeader = ResourceHelper.GetString("OperationsView_MaterialHeader", "Material");
            var categoryHeader = ResourceHelper.GetString("OperationsView_Category", "Category");
            var qtyHeader = ResourceHelper.GetString("OperationsView_Qty", "Qty");
            var unitHeader = ResourceHelper.GetString("OperationsView_Unit", "Unit");
            var unitPriceHeader = ResourceHelper.GetString("OperationsView_UnitPrice", "Unit Price");
            var totalHeader = ResourceHelper.GetString("OperationsView_Total", "Total");

            ws.Cell(6, 1).Value = materialHeader;
            ws.Cell(6, 2).Value = categoryHeader;
            ws.Cell(6, 3).Value = qtyHeader;
            ws.Cell(6, 4).Value = unitHeader;
            ws.Cell(6, 5).Value = unitPriceHeader;
            ws.Cell(6, 6).Value = totalHeader;
            var materialHeaderRange = ws.Range(6, 1, 6, 6);
            materialHeaderRange.Style.Font.Bold = true;
            materialHeaderRange.Style.Font.FontColor = MaterialHeaderFontColor;
            materialHeaderRange.Style.Fill.BackgroundColor = MaterialHeaderFillColor;
            materialHeaderRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            materialHeaderRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(6).Height = 20;

            int row = 7;
            foreach (var usage in usages)
            {
                var unitPrice = ComputeUnitPriceForUsage(usage, freezePrices);
                ws.Cell(row, 1).Value = usage.Material?.Name ?? string.Empty;
                ws.Cell(row, 2).Value = usage.Material?.MaterialCategory?.Name ?? string.Empty;
                ws.Cell(row, 3).Value = usage.Qty;
                ws.Cell(row, 4).Value = usage.Material?.Unit ?? string.Empty;
                ws.Cell(row, 5).Value = unitPrice;
                ws.Cell(row, 6).Value = usage.Qty * unitPrice;
                row++;
            }

            ws.Column(3).Style.NumberFormat.Format = "#,##0.##";
            ws.Column(5).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(6).Style.NumberFormat.Format = "#,##0.00";
            ApplyAlternatingRowStyles(ws, 7, row - 1, 1, 6, MaterialRowPrimaryFill, MaterialRowSecondaryFill);
            ws.Columns(1, 6).AdjustToContents();

            workbook.SaveAs(sfd.FileName);
            TryOpenExportedFile(sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_ExportMaterialsFailedFormat", "Failed to export materials:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ExportStageMaterials_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null)
        {
            return;
        }

        int? stageId = _currentStageId;
        if (!stageId.HasValue)
        {
            if (StagesGrid.SelectedItem is Stage stageEntity)
            {
                stageId = stageEntity.Id;
            }
            else if (StagesGrid.SelectedItem != null)
            {
                try
                {
                    stageId = (int)((dynamic)StagesGrid.SelectedItem).Id;
                }
                catch
                {
                    stageId = null;
                }
            }
        }

        if (!stageId.HasValue)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectStageFirst", "Select a stage first."),
                ResourceHelper.GetString("OperationsView_NoStageTitle", "No stage"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var stage = await _db.Stages
            .Include(s => s.Building)
                .ThenInclude(b => b.Project)
            .Include(s => s.SubStages)
                .ThenInclude(ss => ss.MaterialUsages)
                    .ThenInclude(mu => mu.Material)
            .Include(s => s.SubStages)
                .ThenInclude(ss => ss.MaterialUsages)
                    .ThenInclude(mu => mu.Material)
                        .ThenInclude(m => m.PriceHistory)
            .Include(s => s.SubStages)
                .ThenInclude(ss => ss.MaterialUsages)
                    .ThenInclude(mu => mu.Material)
                        .ThenInclude(m => m.MaterialCategory)
            .FirstOrDefaultAsync(s => s.Id == stageId.Value);

        if (stage == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectStageFirst", "Select a stage first."),
                ResourceHelper.GetString("OperationsView_NoStageTitle", "No stage"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var orderedSubStages = stage.SubStages?
            .OrderBy(ss => ss.OrderIndex)
            .ToList() ?? new List<SubStage>();

        var allUsages = orderedSubStages
            .SelectMany(ss => ss.MaterialUsages.Select(mu => new { SubStage = ss, Usage = mu }))
            .ToList();

        if (allUsages.Count == 0)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_NoMaterialsToExport", "There are no materials to export."),
                ResourceHelper.GetString("OperationsView_NoMaterialsTitle", "No materials"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var projectLabel = ResourceHelper.GetString("OperationsView_ProjectLabel", "Project");
        var lotLabel = ResourceHelper.GetString("OperationsView_LotLabel", "Lot");
        var stageLabel = ResourceHelper.GetString("OperationsView_StageLabel", "Stage");
        var subStageLabel = ResourceHelper.GetString("OperationsView_SubStageLabel", "Sub-stage");

        var stageName = stage.Name ?? string.Empty;
        var buildingCode = stage.Building?.Code ?? string.Empty;
        var projectName = stage.Building?.Project?.Name ?? _currentProject?.Name ?? string.Empty;

        var fileNameBase = $"{SanitizeFileName(buildingCode, lotLabel)}_{SanitizeFileName(stageName, stageLabel)}_{DateTime.Now:yyyyMMdd_HHmm}";

        var sfd = new SaveFileDialog
        {
            Title = ResourceHelper.GetString("OperationsView_ExportStageMaterialsTitle", "Export stage materials"),
            Filter = ResourceHelper.GetString("OperationsView_ExportMaterialsFilter", "Excel files (*.xlsx)|*.xlsx"),
            FileName = $"{fileNameBase}.xlsx"
        };

        var owner = Window.GetWindow(this);
        if (sfd.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            using var workbook = new XLWorkbook();
            var worksheetName = ResourceHelper.GetString("OperationsView_Materials", "Materials");
            if (string.IsNullOrWhiteSpace(worksheetName))
            {
                worksheetName = "Materials";
            }

            var ws = workbook.AddWorksheet(worksheetName);

            WriteInfoRow(ws, 1, projectLabel, projectName);
            WriteInfoRow(ws, 2, lotLabel, buildingCode);
            WriteInfoRow(ws, 3, stageLabel, stageName);

            var materialHeader = ResourceHelper.GetString("OperationsView_MaterialHeader", "Material");
            var categoryHeader = ResourceHelper.GetString("OperationsView_Category", "Category");
            var qtyHeader = ResourceHelper.GetString("OperationsView_Qty", "Qty");
            var unitHeader = ResourceHelper.GetString("OperationsView_Unit", "Unit");
            var unitPriceHeader = ResourceHelper.GetString("OperationsView_UnitPrice", "Unit Price");
            var totalHeader = ResourceHelper.GetString("OperationsView_Total", "Total");

            ws.Cell(5, 1).Value = subStageLabel;
            ws.Cell(5, 2).Value = materialHeader;
            ws.Cell(5, 3).Value = categoryHeader;
            ws.Cell(5, 4).Value = qtyHeader;
            ws.Cell(5, 5).Value = unitHeader;
            ws.Cell(5, 6).Value = unitPriceHeader;
            ws.Cell(5, 7).Value = totalHeader;
            var materialHeaderRange = ws.Range(5, 1, 5, 7);
            materialHeaderRange.Style.Font.Bold = true;
            materialHeaderRange.Style.Font.FontColor = MaterialHeaderFontColor;
            materialHeaderRange.Style.Fill.BackgroundColor = MaterialHeaderFillColor;
            materialHeaderRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            materialHeaderRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(5).Height = 20;

            int row = 6;
            foreach (var subStage in orderedSubStages)
            {
                var subStageUsages = subStage.MaterialUsages
                    .OrderBy(mu => mu.Material?.Name)
                    .ToList();

                if (subStageUsages.Count == 0)
                {
                    continue;
                }

                bool freezePrices = subStage.Status == WorkStatus.Finished || subStage.Status == WorkStatus.Paid;
                int startRow = row;

                foreach (var usage in subStageUsages)
                {
                    var unitPrice = ComputeUnitPriceForUsage(usage, freezePrices);
                    ws.Cell(row, 2).Value = usage.Material?.Name ?? string.Empty;
                    ws.Cell(row, 3).Value = usage.Material?.MaterialCategory?.Name ?? string.Empty;
                    ws.Cell(row, 4).Value = usage.Qty;
                    ws.Cell(row, 5).Value = usage.Material?.Unit ?? string.Empty;
                    ws.Cell(row, 6).Value = unitPrice;
                    ws.Cell(row, 7).Value = usage.Qty * unitPrice;
                    row++;
                }

                ws.Range(startRow, 1, row - 1, 1).Merge();
                ws.Cell(startRow, 1).Value = subStage.Name ?? string.Empty;
                ws.Cell(startRow, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            ws.Column(4).Style.NumberFormat.Format = "#,##0.##";
            ws.Column(6).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(7).Style.NumberFormat.Format = "#,##0.00";
            ApplyAlternatingRowStyles(ws, 6, row - 1, 1, 7, MaterialRowPrimaryFill, MaterialRowSecondaryFill);
            ws.Columns(1, 7).AdjustToContents();

            workbook.SaveAs(sfd.FileName);
            TryOpenExportedFile(sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_ExportMaterialsFailedFormat", "Failed to export materials:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void AddSubStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        // Identify selected Stage (entity preferred)
        int stageId;
        var stageEntity = StagesGrid.SelectedItem as Stage;
        if (stageEntity != null)
        {
            stageId = stageEntity.Id;
        }
        else
        {
            // fallback if the grid is bound to a projection
            var dyn = StagesGrid.SelectedItem as dynamic;
            if (dyn == null) return;
            stageId = (int)dyn.Id;
        }

        // Count current sub-stages to suggest max order
        var currentCount = await _db.SubStages.CountAsync(x => x.StageId == stageId);

        // Open dialog
        var dlg = new Kanstraction.Views.AddSubStageDialog(currentCount)
        {
            Owner = Window.GetWindow(this)
        };

        if (dlg.ShowDialog() != true) return;

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Clamp order again server-side: 1..currentCount+1
            var desiredOrder = Math.Max(1, Math.Min(dlg.OrderIndex, currentCount + 1));

            // Shift existing ≥ desiredOrder
            var toShift = await _db.SubStages
                .Where(ss => ss.StageId == stageId && ss.OrderIndex >= desiredOrder)
                .OrderBy(ss => ss.OrderIndex)
                .ToListAsync();

            foreach (var s in toShift)
                s.OrderIndex += 1;

            // Create new sub-stage (instance-only)
            var newSub = new SubStage
            {
                StageId = stageId,
                Name = dlg.SubStageName,
                OrderIndex = desiredOrder,
                LaborCost = dlg.LaborCost,
                Status = WorkStatus.NotStarted
            };
            _db.SubStages.Add(newSub);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            // Reload sub-stages for this stage and select the new one
            var subs = await _db.SubStages
                .Where(ss => ss.StageId == stageId)
                .OrderBy(ss => ss.OrderIndex)
                .ToListAsync();

            SubStagesGrid.ItemsSource = subs;
            UpdateSubStageLaborTotal(subs);

            var select = subs.FirstOrDefault(x => x.Id == newSub.Id);
            if (select != null)
            {
                SubStagesGrid.SelectedItem = select;
                SubStagesGrid.ScrollIntoView(select);
            }

            // Optionally clear materials and wait for user to add
            MaterialsGrid.ItemsSource = null;
            UpdateMaterialsTotal(null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_AddSubStageFailedFormat", "Failed to add sub-stage:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void DeleteSubStageInstance_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        if (sender is not Button btn) return;

        if (btn.Tag is not int subStageId) return;

        var subStage = await _db.SubStages
            .Include(ss => ss.Stage)
                .ThenInclude(st => st.SubStages)
            .Include(ss => ss.Stage.Building)
                .ThenInclude(b => b.Stages)
            .FirstOrDefaultAsync(ss => ss.Id == subStageId);

        if (subStage == null) return;

        var confirm = MessageBox.Show(
            string.Format(ResourceHelper.GetString("OperationsView_DeleteSubStageConfirmFormat", "Delete sub-stage '{0}'?"), subStage.Name),
            ResourceHelper.GetString("OperationsView_DeleteSubStageTitle", "Delete sub-stage"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var stage = subStage.Stage;
            if (stage == null)
            {
                stage = await _db.Stages
                    .Include(st => st.SubStages)
                    .FirstOrDefaultAsync(st => st.Id == subStage.StageId);

                if (stage == null)
                {
                    throw new InvalidOperationException($"Stage {subStage.StageId} could not be loaded for sub-stage {subStage.Id}.");
                }

                subStage.Stage = stage;
            }

            var building = stage.Building;
            if (building == null)
            {
                building = await _db.Buildings
                    .Include(b => b.Stages)
                    .FirstOrDefaultAsync(b => b.Id == stage.BuildingId);

                if (building != null)
                {
                    stage.Building = building;
                }
            }

            int stageId = stage.Id;
            int buildingId = stage.BuildingId;
            int removedOrder = subStage.OrderIndex;

            var siblingsToShift = stage.SubStages?
                .Where(ss => ss.Id != subStage.Id && ss.OrderIndex > removedOrder)
                .ToList();

            if (siblingsToShift != null)
            {
                foreach (var other in siblingsToShift)
                {
                    other.OrderIndex -= 1;
                }
            }

            stage.SubStages?.Remove(subStage);
            _db.SubStages.Remove(subStage);

            UpdateStageStatusFromSubStages(stage);
            if (building != null)
            {
                UpdateBuildingStatusFromStages(building);
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await ReloadStagesAndSubStagesAsync(buildingId, stageId);
            await ReloadBuildingsAsync(buildingId, stageId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_DeleteSubStageFailedFormat", "Failed to delete sub-stage:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void GenerateProgressReport_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null || _currentProject == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectProjectFirst", "Select a project first."),
                ResourceHelper.GetString("OperationsView_NoProjectTitle", "No project"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new ReportDurationDialog
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await GenerateProgressReportAsync(dialog.StartDate, dialog.EndDate);
    }

    private void ReportsMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private async void GenerateRemainingLaborReport_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null || _currentProject == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectProjectFirst", "Select a project first."),
                ResourceHelper.GetString("OperationsView_NoProjectTitle", "No project"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await GenerateRemainingCostReportAsync(RemainingCostReportType.Labor);
    }

    private async void GenerateRemainingMaterialReport_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null || _currentProject == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectProjectFirst", "Select a project first."),
                ResourceHelper.GetString("OperationsView_NoProjectTitle", "No project"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await GenerateRemainingCostReportAsync(RemainingCostReportType.Material);
    }

    private async void ResolvePayment_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null || _currentProject == null)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_SelectProjectFirst", "Select a project first."),
                ResourceHelper.GetString("OperationsView_NoProjectTitle", "No project"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // 1) Gather eligible sub-stages: Finished (not Paid/Stopped) for current project
        var eligible = await _db.SubStages
            .Where(ss => ss.Stage.Building.ProjectId == _currentProject.Id
                      && ss.Status == WorkStatus.Finished)
            .Include(ss => ss.Stage)
                .ThenInclude(st => st.Building)
            .OrderBy(ss => ss.Stage.Building.Code)
            .ThenBy(ss => ss.Stage.OrderIndex)
            .ThenBy(ss => ss.OrderIndex)
            .ToListAsync();

        if (eligible.Count == 0)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_NoCompletedSubStages", "No completed sub-stages to resolve."),
                ResourceHelper.GetString("OperationsView_NothingToDoTitle", "Nothing to do"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // 2) Build report DTO (grouped) for PDF
        var data = new PaymentReportRenderer.ReportData
        {
            ProjectName = _currentProject.Name,
            GeneratedAt = DateTime.Now
        };

        var byBuilding = eligible.GroupBy(ss => ss.Stage.Building);
        foreach (var bgroup in byBuilding)
        {
            var b = bgroup.Key; // Building
            var bDto = new PaymentReportRenderer.BuildingGroup { BuildingCode = b.Code };

            var byStage = bgroup.GroupBy(ss => ss.Stage)
                                .OrderBy(g => g.Key.OrderIndex);

            foreach (var sgroup in byStage)
            {
                var st = sgroup.Key; // Stage
                var sDto = new PaymentReportRenderer.StageGroup { StageName = st.Name };

                foreach (var ss in sgroup.OrderBy(x => x.OrderIndex))
                {
                    sDto.Items.Add(new PaymentReportRenderer.SubStageRow
                    {
                        SubStageName = ss.Name,
                        StageName = st.Name,
                        LaborCost = ss.LaborCost
                    });
                }

                sDto.StageSubtotal = sDto.Items.Sum(i => i.LaborCost);
                bDto.Stages.Add(sDto);
            }

            bDto.BuildingSubtotal = bDto.Stages.Sum(s => s.StageSubtotal);
            data.Buildings.Add(bDto);
        }

        data.GrandTotal = data.Buildings.Sum(b => b.BuildingSubtotal);

        // 3) Ask where to save
        var sfd = new SaveFileDialog
        {
            Title = ResourceHelper.GetString("OperationsView_SavePaymentReportTitle", "Save payment resolution report"),
            Filter = "PDF files (*.pdf)|*.pdf",
            FileName = $"Payment_{_currentProject.Name}_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
        };
        if (sfd.ShowDialog(Window.GetWindow(this)) != true)
            return;

        // 4) Render PDF (temp → final inside renderer)
        try
        {
            var renderer = new PaymentReportRenderer();
            renderer.Render(data, sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_GeneratePdfFailedFormat", "Failed to generate PDF:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // 5) On success, mark as Paid and cascade (transaction)
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Mark each eligible sub-stage as Paid
            foreach (var ss in eligible)
            {
                ss.Status = WorkStatus.Paid;
                // keep EndDate as is (already set when finished)
            }

            // Cascade: update each involved Stage and Building
            // We already have Stage and Building loaded for these sub-stages
            var affectedStages = eligible.Select(e1 => e1.Stage).Distinct().ToList();
            foreach (var st in affectedStages)
            {
                // Make sure st.SubStages is loaded (it is, because eligible share the same context; but ensure anyway)
                await _db.Entry(st).Collection(x => x.SubStages).LoadAsync();
                UpdateStageStatusFromSubStages(st); // your existing helper
            }

            var affectedBuildings = eligible.Select(e1 => e1.Stage.Building).Distinct().ToList();
            foreach (var b in affectedBuildings)
            {
                await _db.Entry(b).Collection(x => x.Stages).LoadAsync();
                UpdateBuildingStatusFromStages(b);  // your existing helper
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_MarkPaidFailedFormat", "Failed to mark items as paid after generating the PDF:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // 6) Refresh UI
        await ReloadBuildingsAsync();
        MessageBox.Show(
            ResourceHelper.GetString("OperationsView_PaymentResolutionCompleted", "Payment resolution completed."),
            ResourceHelper.GetString("OperationsView_PaymentResolutionCompletedTitle", "Done"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // Small DTO matching the dialog's PresetVm
    private sealed class StagePresetPick
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private async void DeleteStage_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        if (sender is not Button btn) return;

        if (btn.Tag is not int stageId) return;

        var stage = await _db.Stages
            .Include(st => st.SubStages)
            .Include(st => st.Building)
                .ThenInclude(b => b.Stages)
                    .ThenInclude(s => s.SubStages)
            .FirstOrDefaultAsync(st => st.Id == stageId);

        if (stage == null) return;

        var confirm = MessageBox.Show(
            string.Format(ResourceHelper.GetString("OperationsView_DeleteStageConfirmFormat", "Delete stage '{0}'?"), stage.Name),
            ResourceHelper.GetString("OperationsView_DeleteStageTitle", "Delete stage"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            int buildingId = stage.BuildingId;
            int removedOrder = stage.OrderIndex;

            foreach (var other in stage.Building.Stages.Where(s => s.Id != stage.Id && s.OrderIndex > removedOrder))
            {
                other.OrderIndex -= 1;
            }

            stage.Building.Stages.Remove(stage);
            _db.Stages.Remove(stage);

            UpdateBuildingStatusFromStages(stage.Building);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await ReloadStagesAndSubStagesAsync(buildingId);
            await ReloadBuildingsAsync(buildingId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_DeleteStageFailedFormat", "Failed to delete stage:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void AddStageToBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        // We need a selected building (your BuildingsGrid binds to an anonymous projection)
        var bRow = BuildingsGrid.SelectedItem as dynamic;
        if (bRow == null) return;

        int buildingId = (int)bRow.Id;

        var buildingTypeId = await _db.Buildings
            .Where(b => b.Id == buildingId)
            .Select(b => b.BuildingTypeId)
            .FirstAsync();

        // Load current building stages (names + count)
        var currentStages = await _db.Stages
            .Where(s => s.BuildingId == buildingId)
            .OrderBy(s => s.OrderIndex)
            .Select(s => new { s.Id, s.Name, s.OrderIndex })
            .ToListAsync();

        var existingNames = currentStages.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int currentCount = currentStages.Count;

        // Available presets = active StagePresets NOT already present by name in this building
        var availablePresets = await _db.StagePresets
            .Where(p => p.IsActive && !existingNames.Contains(p.Name))
            .OrderBy(p => p.Name)
            .Select(p => new StagePresetPick { Id = p.Id, Name = p.Name })
            .ToListAsync();

        if (availablePresets.Count == 0)
        {
            MessageBox.Show(
                ResourceHelper.GetString("OperationsView_NoPresetToAddMessage", "All active stage presets are already in this building."),
                ResourceHelper.GetString("OperationsView_NoPresetTitle", "No preset"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Open dialog
        var dlg = new Kanstraction.Views.AddStageToBuildingDialog(
            availablePresets.ConvertAll(p => new Kanstraction.Views.AddStageToBuildingDialog.PresetVm { Id = p.Id, Name = p.Name }),
            currentCount)
        {
            Owner = Window.GetWindow(this)
        };

        if (dlg.ShowDialog() != true) return;

        int presetId = dlg.SelectedPresetId!.Value;
        int desiredOrder = dlg.OrderIndex; // already clamped by dialog

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Shift existing stages with OrderIndex >= desiredOrder
            var toShift = await _db.Stages
                .Where(s => s.BuildingId == buildingId && s.OrderIndex >= desiredOrder)
                .OrderBy(s => s.OrderIndex)
                .ToListAsync();

            foreach (var s in toShift)
                s.OrderIndex += 1;

            // Resolve the preset's name
            var presetName = await _db.StagePresets
                .Where(p => p.Id == presetId)
                .Select(p => p.Name)
                .FirstAsync();

            // Create the new Stage (instance-only)
            var newStage = new Stage
            {
                BuildingId = buildingId,
                Name = presetName,
                OrderIndex = desiredOrder,
                Status = WorkStatus.NotStarted
            };
            _db.Stages.Add(newStage);
            await _db.SaveChangesAsync(); // need newStage.Id for children

            // Clone SubStagePresets (+ material usage presets) into instance
            var subPresets = await _db.SubStagePresets
                .Where(sp => sp.StagePresetId == presetId)
                .OrderBy(sp => sp.OrderIndex)
                .ToListAsync();

            var subPresetIds = subPresets.ConvertAll(sp => sp.Id);

            var laborLookup = await _db.BuildingTypeSubStageLabors
                .Where(x => x.BuildingTypeId == buildingTypeId && subPresetIds.Contains(x.SubStagePresetId) && x.LaborCost.HasValue)
                .ToDictionaryAsync(x => x.SubStagePresetId, x => x.LaborCost!.Value);

            var materialUsageLookup = await _db.BuildingTypeMaterialUsages
                .Where(x => x.BuildingTypeId == buildingTypeId && subPresetIds.Contains(x.SubStagePresetId) && x.Qty.HasValue)
                .ToDictionaryAsync(x => (x.SubStagePresetId, x.MaterialId), x => x.Qty!.Value);

            foreach (var sp in subPresets)
            {
                var sub = new SubStage
                {
                    StageId = newStage.Id,
                    Name = sp.Name,
                    OrderIndex = sp.OrderIndex,
                    Status = WorkStatus.NotStarted,
                    LaborCost = laborLookup.TryGetValue(sp.Id, out var labor)
                        ? labor
                        : 0m
                };
                _db.SubStages.Add(sub);
                await _db.SaveChangesAsync(); // need sub.Id to attach usages

                var muPresets = await _db.MaterialUsagesPreset
                    .Where(mup => mup.SubStagePresetId == sp.Id)
                    .ToListAsync();

                foreach (var mup in muPresets)
                {
                    var qty = materialUsageLookup.TryGetValue((sp.Id, mup.MaterialId), out var buildingTypeQty)
                        ? buildingTypeQty
                        : 0m;

                    var mu = new MaterialUsage
                    {
                        SubStageId = sub.Id,
                        MaterialId = mup.MaterialId,
                        Qty = qty,
                        UsageDate = DateTime.Today,
                        Notes = null
                    };
                    _db.MaterialUsages.Add(mu);
                }
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            // Refresh UI: reload stages for this building and select the new stage
            await ReloadStagesAndSubStagesAsync(buildingId, newStage.Id);
            // Also refresh buildings row (progress / current stage)
            await ReloadBuildingsAsync(buildingId, newStage.Id);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("OperationsView_AddStageFailedFormat", "Failed to add stage:\n{0}"), ex.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

}
