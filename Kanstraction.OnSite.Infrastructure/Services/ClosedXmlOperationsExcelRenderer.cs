using ClosedXML.Excel;
using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Localization;
using Kanstraction.OnSite.Application.Reports.Models;

namespace Kanstraction.OnSite.Infrastructure.Services;

public sealed class ClosedXmlOperationsExcelRenderer : IOperationsExcelRenderer
{
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

    public Task RenderAsync(
        OperationsExcelReport report,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var workbook = new XLWorkbook();
        switch (report)
        {
            case CompensationExcelReport compensation:
                RenderCompensation(workbook, compensation);
                break;
            case ProgressExcelReport progress:
                RenderProgress(workbook, progress);
                break;
            case RemainingCostExcelReport remaining:
                RenderRemainingCosts(workbook, remaining);
                break;
            case StageSubStagesExcelReport subStages:
                RenderSubStages(workbook, subStages);
                break;
            case MaterialsExcelReport materials:
                RenderMaterials(workbook, materials);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported operations report type: {report.GetType().Name}.");
        }

        workbook.SaveAs(destinationPath);
        return Task.CompletedTask;
    }

    private static void RenderCompensation(
        XLWorkbook workbook,
        CompensationExcelReport report)
    {
        var data = report.Data;
        var ws = workbook.Worksheets.Add(SanitizeWorksheetName(
            L("OperationsView_CompensationWorksheetName", "Compensation"),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        var title = $"{L("OperationsView_CompensationTitlePrefix", "Compensation lot")} {data.BuildingCode}".Trim();
        var titleRange = ws.Range(1, 3, 1, 4);
        titleRange.Merge();
        titleRange.Value = title;
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 16;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 24;

        const int headerRow = 3;
        ws.Cell(headerRow, 1).Value = L("OperationsView_SubStageLabel", "Sub-stage");
        ws.Cell(headerRow, 2).Value = L("OperationsView_Labor", "Labor");
        ws.Cell(headerRow, 3).Value = L("OperationsView_MaterialsCost", "Materials cost");
        StyleHeader(ws.Range(headerRow, 1, headerRow, 3), SubStageHeaderFillColor, SubStageHeaderFontColor);

        var row = headerRow + 1;
        decimal totalLabor = 0m;
        decimal totalMaterials = 0m;
        foreach (var entry in data.Entries
                     .OrderBy(entry => entry.StageOrder)
                     .ThenBy(entry => entry.SubStageOrder))
        {
            ws.Cell(row, 1).Value = string.IsNullOrWhiteSpace(entry.StageName)
                ? entry.SubStageName
                : $"{entry.SubStageName} ({entry.StageName})";
            ws.Cell(row, 2).Value = entry.LaborCost;
            ws.Cell(row, 3).Value = entry.MaterialCost;
            totalLabor += entry.LaborCost;
            totalMaterials += entry.MaterialCost;
            row++;
        }

        ApplyAlternatingRowStyles(
            ws,
            headerRow + 1,
            row - 1,
            1,
            3,
            SubStageRowPrimaryFill,
            SubStageRowSecondaryFill);
        ws.Cell(row, 1).Value = L("OperationsView_Total", "Total");
        ws.Cell(row, 2).Value = totalLabor;
        ws.Cell(row, 3).Value = totalMaterials;
        ws.Cell(row, 4).Value = totalLabor + totalMaterials;
        StyleHeader(ws.Range(row, 1, row, 4), SubStageHeaderFillColor, SubStageHeaderFontColor);
        ws.Columns(2, 4).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns(1, 4).AdjustToContents();
    }

    private static void RenderProgress(
        XLWorkbook workbook,
        ProgressExcelReport report)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in report.Sheets.Where(sheet => sheet.Rows.Count > 0))
        {
            var ws = workbook.Worksheets.Add(SanitizeWorksheetName(sheet.Name, names));
            var totalColumns = Math.Max(4, sheet.Columns.Count + 1);
            var projectRange = ws.Range(1, Math.Min(3, totalColumns), 1, Math.Min(4, totalColumns));
            projectRange.Merge();
            projectRange.Value = report.ProjectName;
            projectRange.Style.Font.Bold = true;
            projectRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = L("ProgressReport_BuildingCodeHeader", "Lot");
            for (var index = 0; index < sheet.Columns.Count; index++)
                ws.Cell(2, index + 2).Value = sheet.Columns[index].Name;
            StyleHeader(
                ws.Range(2, 1, 2, totalColumns),
                ProgressHeaderFillColor,
                ProgressHeaderFontColor);
            ws.SheetView.FreezeRows(2);

            var row = 3;
            foreach (var item in sheet.Rows)
            {
                ws.Cell(row, 1).Value = item.BuildingCode;
                for (var index = 0; index < item.Values.Count; index++)
                    ws.Cell(row, index + 2).Value = item.Values[index];
                row++;
            }
            ApplyAlternatingRowStyles(
                ws,
                3,
                row - 1,
                1,
                totalColumns,
                ProgressRowPrimaryFill,
                ProgressRowSecondaryFill);
            ApplyBorders(ws, Math.Max(2, row - 1), totalColumns);
            ws.Columns(1, totalColumns).AdjustToContents();
        }
    }

    private static void RenderRemainingCosts(
        XLWorkbook workbook,
        RemainingCostExcelReport report)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in report.Sheets)
        {
            var ws = workbook.Worksheets.Add(SanitizeWorksheetName(sheet.Name, names));
            var totalColumns = Math.Max(4, sheet.StageNames.Count + 2);
            var titleRange = ws.Range(1, Math.Min(3, totalColumns), 1, Math.Min(4, totalColumns));
            titleRange.Merge();
            titleRange.Value = report.Title;
            titleRange.Style.Font.Bold = true;
            titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = L("ProgressReport_BuildingCodeHeader", "Lot");
            for (var index = 0; index < sheet.StageNames.Count; index++)
                ws.Cell(2, index + 2).Value = sheet.StageNames[index];
            ws.Cell(2, totalColumns).Value = L("RemainingCostReport_TotalHeader", "Total remaining");
            StyleHeader(
                ws.Range(2, 1, 2, totalColumns),
                ProgressHeaderFillColor,
                ProgressHeaderFontColor);
            ws.SheetView.FreezeRows(2);

            var row = 3;
            foreach (var item in sheet.Rows)
            {
                ws.Cell(row, 1).Value = item.BuildingCode;
                for (var index = 0; index < item.Values.Count; index++)
                    ws.Cell(row, index + 2).Value = item.Values[index];
                ws.Cell(row, totalColumns).Value = item.Total;
                row++;
            }
            ApplyAlternatingRowStyles(
                ws,
                3,
                row - 1,
                1,
                totalColumns,
                ProgressRowPrimaryFill,
                ProgressRowSecondaryFill);
            ws.Columns(2, totalColumns).Style.NumberFormat.Format = "#,##0.00";
            ApplyBorders(ws, Math.Max(2, row - 1), totalColumns);
            ws.Columns(1, totalColumns).AdjustToContents();
        }
    }

    private static void RenderSubStages(
        XLWorkbook workbook,
        StageSubStagesExcelReport report)
    {
        var ws = workbook.Worksheets.Add(L("OperationsView_SubStages", "Sub-stages"));
        WriteInfoRow(ws, 1, L("OperationsView_ProjectLabel", "Project"), report.ProjectName);
        WriteInfoRow(ws, 2, L("OperationsView_LotLabel", "Lot"), report.BuildingCode);
        WriteInfoRow(ws, 3, L("OperationsView_StageLabel", "Stage"), report.StageName);

        ws.Cell(5, 1).Value = L("OperationsView_Name", "Name");
        ws.Cell(5, 2).Value = L("OperationsView_Status", "Status");
        ws.Cell(5, 3).Value = L("OperationsView_Labor", "Labor");
        StyleHeader(ws.Range(5, 1, 5, 3), SubStageHeaderFillColor, SubStageHeaderFontColor);

        var row = 6;
        foreach (var item in report.Rows)
        {
            ws.Cell(row, 1).Value = item.Name;
            ws.Cell(row, 2).Value = item.Status;
            ws.Cell(row, 3).Value = item.LaborCost;
            row++;
        }
        ws.Column(3).Style.NumberFormat.Format = "#,##0.00";
        ApplyAlternatingRowStyles(
            ws,
            6,
            row - 1,
            1,
            3,
            SubStageRowPrimaryFill,
            SubStageRowSecondaryFill);
        ws.Columns(1, 3).AdjustToContents();
    }

    private static void RenderMaterials(
        XLWorkbook workbook,
        MaterialsExcelReport report)
    {
        var ws = workbook.Worksheets.Add(L("OperationsView_Materials", "Materials"));
        WriteInfoRow(ws, 1, L("OperationsView_ProjectLabel", "Project"), report.ProjectName);
        WriteInfoRow(ws, 2, L("OperationsView_LotLabel", "Lot"), report.BuildingCode);
        WriteInfoRow(ws, 3, L("OperationsView_StageLabel", "Stage"), report.StageName);

        var hasSubStageColumn = report.SubStageName == null;
        var headerRow = hasSubStageColumn ? 5 : 6;
        if (!hasSubStageColumn)
        {
            WriteInfoRow(
                ws,
                4,
                L("OperationsView_SubStageLabel", "Sub-stage"),
                report.SubStageName ?? string.Empty);
        }

        var column = 1;
        if (hasSubStageColumn)
            ws.Cell(headerRow, column++).Value = L("OperationsView_SubStageLabel", "Sub-stage");
        ws.Cell(headerRow, column++).Value = L("OperationsView_MaterialHeader", "Material");
        ws.Cell(headerRow, column++).Value = L("OperationsView_Category", "Category");
        ws.Cell(headerRow, column++).Value = L("OperationsView_Qty", "Qty");
        ws.Cell(headerRow, column++).Value = L("OperationsView_Unit", "Unit");
        ws.Cell(headerRow, column++).Value = L("OperationsView_UnitPrice", "Unit Price");
        ws.Cell(headerRow, column).Value = L("OperationsView_Total", "Total");
        StyleHeader(
            ws.Range(headerRow, 1, headerRow, column),
            MaterialHeaderFillColor,
            MaterialHeaderFontColor);

        var row = headerRow + 1;
        foreach (var item in report.Rows)
        {
            column = 1;
            if (hasSubStageColumn)
                ws.Cell(row, column++).Value = item.SubStageName ?? string.Empty;
            ws.Cell(row, column++).Value = item.MaterialName;
            ws.Cell(row, column++).Value = item.CategoryName;
            ws.Cell(row, column++).Value = item.Quantity;
            ws.Cell(row, column++).Value = item.Unit;
            ws.Cell(row, column++).Value = item.UnitPrice;
            ws.Cell(row, column).Value = item.Total;
            row++;
        }

        ws.Columns(column - 1, column).Style.NumberFormat.Format = "#,##0.00";
        ApplyAlternatingRowStyles(
            ws,
            headerRow + 1,
            row - 1,
            1,
            column,
            MaterialRowPrimaryFill,
            MaterialRowSecondaryFill);
        ws.Columns(1, column).AdjustToContents();
    }

    private static void WriteInfoRow(
        IXLWorksheet ws,
        int rowNumber,
        string label,
        string value)
    {
        var labelCell = ws.Cell(rowNumber, 1);
        var valueCell = ws.Cell(rowNumber, 2);
        labelCell.Value = label;
        labelCell.Style.Font.Bold = true;
        labelCell.Style.Font.FontColor = InfoLabelFontColor;
        labelCell.Style.Fill.BackgroundColor = InfoLabelFillColor;
        valueCell.Value = value;
        valueCell.Style.Fill.BackgroundColor = InfoValueFillColor;
        ws.Row(rowNumber).Height = 22;
    }

    private static void StyleHeader(
        IXLRange range,
        XLColor fill,
        XLColor font)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = font;
        range.Style.Fill.BackgroundColor = fill;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void ApplyAlternatingRowStyles(
        IXLWorksheet ws,
        int startRow,
        int endRow,
        int startColumn,
        int endColumn,
        XLColor firstColor,
        XLColor secondColor)
    {
        for (var row = startRow; row <= endRow; row++)
        {
            var range = ws.Range(row, startColumn, row, endColumn);
            range.Style.Fill.BackgroundColor =
                (row - startRow) % 2 == 0 ? firstColor : secondColor;
        }
    }

    private static void ApplyBorders(
        IXLWorksheet ws,
        int lastRow,
        int lastColumn)
    {
        var range = ws.Range(1, 1, lastRow, lastColumn);
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    private static string SanitizeWorksheetName(
        string? input,
        ISet<string> existingNames)
    {
        var invalidChars = new HashSet<char>(['[', ']', '*', '?', '/', '\\', ':']);
        var baseName = string.IsNullOrWhiteSpace(input) ? "Sheet" : input.Trim();
        baseName = new string(baseName
            .Select(character => invalidChars.Contains(character) ? '_' : character)
            .ToArray());
        if (baseName.Length > 31)
            baseName = baseName[..31];

        var candidate = baseName;
        var counter = 1;
        while (existingNames.Contains(candidate))
        {
            var suffix = $"_{counter++}";
            candidate = baseName[..Math.Min(baseName.Length, 31 - suffix.Length)] + suffix;
        }
        existingNames.Add(candidate);
        return candidate;
    }

    private static string L(string key, string fallback)
        => StringLocalizer.GetString(key, fallback);
}
