using Kanstraction.OnSite.Application.Reports.Models;
using Kanstraction.OnSite.Application.Reports.Queries.GetOperationsReportData;
using Kanstraction.OnSite.Application.Common;

namespace Kanstraction.OnSite.Application.Reports.Services;

public static class OperationsReportBuilder
{
    public static ProgressExcelReport BuildProgress(
        ProjectOperationsReportDataDto data,
        DateTime startDate,
        DateTime endDate,
        string stoppedLabel)
    {
        var sheets = new List<ProgressReportSheet>();
        foreach (var type in data.BuildingTypes.OrderBy(type => type.Name))
        {
            var columns = type.Stages
                .OrderBy(stage => stage.OrderIndex)
                .SelectMany(stage =>
                {
                    var ordered = stage.SubStages
                        .OrderBy(subStage => subStage.OrderIndex)
                        .ToList();
                    return ordered
                        .Where(subStage =>
                            subStage.IncludeInReport ??
                            subStage.Id == ordered.LastOrDefault()?.Id)
                        .Select((subStage, index) => new ColumnDefinition(
                            stage.Name,
                            stage.OrderIndex,
                            subStage.Name,
                            subStage.OrderIndex,
                            index + 1));
                })
                .ToList();

            var rows = new List<ProgressReportRow>();
            foreach (var building in data.Buildings
                         .Where(building => building.BuildingTypeId == type.Id)
                         .OrderBy(building => building.Code, StringComparer.CurrentCultureIgnoreCase))
            {
                if (ShouldSkipBuilding(building, startDate, endDate))
                    continue;

                var values = columns.Select(column =>
                {
                    var stage = FindStage(building, column);
                    var subStage = FindSubStage(stage, column);
                    return subStage?.Status switch
                    {
                        WorkStatusDto.Paid => "100%",
                        WorkStatusDto.Stopped => $"100% ({stoppedLabel})",
                        _ => string.Empty
                    };
                }).ToList();
                rows.Add(new ProgressReportRow(building.Code, values));
            }

            sheets.Add(new ProgressReportSheet(
                type.Name,
                columns.Select(column => new ProgressReportColumn(column.SubStageName)).ToList(),
                rows));
        }

        return new ProgressExcelReport(data.ProjectName, sheets);
    }

    public static RemainingCostExcelReport BuildRemainingCosts(
        ProjectOperationsReportDataDto data,
        RemainingCostKind kind,
        string title)
    {
        var sheets = data.BuildingTypes
            .OrderBy(type => type.Name)
            .Select(type =>
            {
                var stages = type.Stages.OrderBy(stage => stage.OrderIndex).ToList();
                var rows = data.Buildings
                    .Where(building => building.BuildingTypeId == type.Id)
                    .OrderBy(building => building.Code, StringComparer.CurrentCultureIgnoreCase)
                    .Select(building =>
                    {
                        var values = stages
                            .Select(template => ComputeRemainingCost(
                                building.Stages.FirstOrDefault(stage =>
                                    string.Equals(
                                        stage.Name,
                                        template.Name,
                                        StringComparison.OrdinalIgnoreCase)) ??
                                building.Stages.FirstOrDefault(stage =>
                                    stage.OrderIndex == template.OrderIndex),
                                kind))
                            .ToList();
                        return new RemainingCostReportRow(
                            building.Code,
                            values,
                            values.Sum());
                    })
                    .ToList();
                return new RemainingCostReportSheet(
                    type.Name,
                    stages.Select(stage => stage.Name).ToList(),
                    rows);
            })
            .Where(sheet => sheet.Rows.Count > 0)
            .ToList();

        return new RemainingCostExcelReport(
            data.ProjectName,
            title,
            kind,
            sheets);
    }

    public static StageSubStagesExcelReport BuildStageSubStages(
        ReportStageDto stage,
        string projectName,
        string buildingCode,
        Func<WorkStatusDto, string> statusFormatter)
        => new(
            projectName,
            buildingCode,
            stage.Name,
            stage.SubStages
                .OrderBy(subStage => subStage.OrderIndex)
                .Select(subStage => new StageSubStageReportRow(
                    subStage.Name,
                    statusFormatter(subStage.Status),
                    subStage.LaborCost))
                .ToList());

    public static MaterialsExcelReport BuildSubStageMaterials(
        ReportSubStageDto subStage,
        string projectName,
        string buildingCode,
        string stageName)
        => new(
            projectName,
            buildingCode,
            stageName,
            subStage.Name,
            subStage.MaterialUsages
                .OrderBy(usage => usage.Material.Name)
                .Select(usage => BuildMaterialRow(subStage, usage, null))
                .ToList());

    public static MaterialsExcelReport BuildStageMaterials(
        ReportStageDto stage,
        string projectName,
        string buildingCode)
        => new(
            projectName,
            buildingCode,
            stage.Name,
            null,
            stage.SubStages
                .OrderBy(subStage => subStage.OrderIndex)
                .SelectMany(subStage => subStage.MaterialUsages
                    .OrderBy(usage => usage.Material.Name)
                    .Select(usage => BuildMaterialRow(
                        subStage,
                        usage,
                        subStage.Name)))
                .ToList());

    private static MaterialReportRow BuildMaterialRow(
        ReportSubStageDto subStage,
        ReportMaterialUsageDto usage,
        string? subStageName)
    {
        var freezePrices = subStage.Status is WorkStatusDto.Finished or WorkStatusDto.Paid;
        var unitPrice = ResolveUnitPrice(usage, freezePrices);
        return new MaterialReportRow(
            subStageName,
            usage.Material.Name,
            usage.Material.CategoryName,
            usage.Qty,
            usage.Material.Unit,
            unitPrice,
            usage.Qty * unitPrice);
    }

    private static decimal ComputeRemainingCost(
        ReportStageDto? stage,
        RemainingCostKind kind)
    {
        if (stage == null || stage.Status is WorkStatusDto.Paid or WorkStatusDto.Stopped)
            return 0m;

        return stage.SubStages
            .Where(subStage => subStage.Status is not (WorkStatusDto.Paid or WorkStatusDto.Stopped))
            .Sum(subStage => kind == RemainingCostKind.Labor
                ? subStage.LaborCost
                : subStage.MaterialUsages.Sum(usage =>
                    usage.Qty * ResolveUnitPrice(
                        usage,
                        subStage.Status is WorkStatusDto.Finished or WorkStatusDto.Paid)));
    }

    private static decimal ResolveUnitPrice(
        ReportMaterialUsageDto usage,
        bool freeze)
    {
        if (!freeze || usage.UsageDate == default)
            return usage.Material.PricePerUnit;

        var usageDate = usage.UsageDate.Date;
        return usage.Material.PriceHistory
            .Where(price =>
                price.StartDate.Date <= usageDate &&
                (!price.EndDate.HasValue || price.EndDate.Value.Date >= usageDate))
            .OrderByDescending(price => price.StartDate)
            .Select(price => (decimal?)price.PricePerUnit)
            .FirstOrDefault()
            ?? usage.Material.PriceHistory
                .Where(price => price.StartDate.Date <= usageDate)
                .OrderByDescending(price => price.StartDate)
                .Select(price => (decimal?)price.PricePerUnit)
                .FirstOrDefault()
            ?? usage.Material.PriceHistory
                .OrderBy(price => price.StartDate)
                .Select(price => (decimal?)price.PricePerUnit)
                .FirstOrDefault()
            ?? usage.Material.PricePerUnit;
    }

    private static bool ShouldSkipBuilding(
        ReportBuildingDto building,
        DateTime startDate,
        DateTime endDate)
    {
        var activityDates = building.Stages
            .SelectMany(stage => new DateTime?[]
            {
                stage.StartDate,
                stage.EndDate
            }.Concat(stage.SubStages.SelectMany(subStage => new DateTime?[]
            {
                subStage.StartDate,
                subStage.EndDate
            })))
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .ToList();
        var firstActivity = activityDates.Count > 0 ? activityDates.Min() : (DateTime?)null;
        if (firstActivity.HasValue && firstActivity.Value.Date > endDate.Date)
            return true;

        if (building.Status is not (WorkStatusDto.Paid or WorkStatusDto.Stopped))
            return false;

        var lastActivity = activityDates.Count > 0 ? activityDates.Max() : (DateTime?)null;
        return lastActivity.HasValue && lastActivity.Value.Date < startDate.Date;
    }

    private static ReportStageDto? FindStage(
        ReportBuildingDto building,
        ColumnDefinition column)
        => building.Stages.FirstOrDefault(stage =>
               stage.OrderIndex == column.StageOrderIndex)
           ?? building.Stages.FirstOrDefault(stage =>
               string.Equals(stage.Name, column.StageName, StringComparison.OrdinalIgnoreCase));

    private static ReportSubStageDto? FindSubStage(
        ReportStageDto? stage,
        ColumnDefinition column)
        => stage?.SubStages.FirstOrDefault(subStage =>
               subStage.OrderIndex == column.SubStageOrderIndex)
           ?? stage?.SubStages.FirstOrDefault(subStage =>
               string.Equals(
                   subStage.Name,
                   column.SubStageName,
                   StringComparison.OrdinalIgnoreCase));

    private sealed record ColumnDefinition(
        string StageName,
        int StageOrderIndex,
        string SubStageName,
        int SubStageOrderIndex,
        int SubStagePosition);
}
