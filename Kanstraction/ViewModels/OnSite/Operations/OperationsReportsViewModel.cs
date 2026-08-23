using Kanstraction.OnSite.Application.Buildings.Commands.StopBuilding;
using Kanstraction.OnSite.Application.Payments.Commands.ResolvePayments;
using Kanstraction.OnSite.Application.Projects.Queries.ListProjects;
using Kanstraction.OnSite.Application.Reports.Commands.ExportOperationsExcel;
using Kanstraction.OnSite.Application.Reports.Models;
using Kanstraction.OnSite.Application.Reports.Queries.GetOperationsReportData;
using Kanstraction.OnSite.Application.Reports.Services;
using Kanstraction.Services;
using System.Globalization;
using System.IO;

namespace Kanstraction.ViewModels.OnSite.Operations;

public enum RemainingCostReportType
{
    Labor,
    Material
}

public sealed class OperationsReportsViewModel(
    IApplicationDispatcher dispatcher,
    IUserInteractionService interaction)
{
    private ProjectListItemDto? _project;
    private string _buildingCode = string.Empty;
    private string _stageName = string.Empty;
    private string _subStageName = string.Empty;

    public void SetProject(ProjectListItemDto? project) => _project = project;

    public void SetSelection(
        string? buildingCode,
        string? stageName,
        string? subStageName)
    {
        _buildingCode = buildingCode ?? string.Empty;
        _stageName = stageName ?? string.Empty;
        _subStageName = subStageName ?? string.Empty;
    }

    public async Task ExportCompensationAsync(CompensationReportData report)
    {
        if (report.Entries.Count == 0)
        {
            return;
        }

        var prefix = ResourceHelper.GetString(
            "OperationsView_CompensationTitlePrefix",
            "Compensation lot");
        var lotLabel = ResourceHelper.GetString("OperationsView_LotLabel", "Lot");
        var defaultName =
            $"{SanitizeFileName(prefix, "Compensation lot")}_{SanitizeFileName(report.BuildingCode, lotLabel)}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        var path = interaction.SelectSavePath(
            ResourceHelper.GetString(
                "OperationsView_CompensationExportTitle",
                "Export compensation"),
            ResourceHelper.GetString(
                "OperationsView_CompensationExportFilter",
                "Excel files (*.xlsx)|*.xlsx"),
            defaultName);
        if (path == null)
        {
            return;
        }

        await ExportAsync(
            new CompensationExcelReport(report),
            path,
            "OperationsView_CompensationExportFailedFormat",
            "Failed to export compensation:\n{0}");
    }

    public async Task GenerateProgressReportAsync()
    {
        if (!EnsureProject())
        {
            return;
        }
        var period = interaction.SelectReportPeriod();
        if (period == null)
        {
            return;
        }

        var data = await dispatcher.Send(
            new GetProjectOperationsReportDataQuery(_project!.Id));
        if (data == null || data.BuildingTypes.Count == 0)
        {
            interaction.Information(
                ResourceHelper.GetString(
                    "ProgressReport_NoBuildingTypesMessage",
                    "This project has no buildings with a building type. Nothing to export."),
                ResourceHelper.GetString(
                    "ProgressReport_NoBuildingTypesTitle",
                    "No building types"));
            return;
        }

        var report = OperationsReportBuilder.BuildProgress(
            data,
            period.StartDate,
            period.EndDate,
            ResourceHelper.GetString("WorkStatus_Stopped", "Stopped"));
        if (!report.Sheets.Any(sheet => sheet.Rows.Count > 0))
        {
            interaction.Information(
                ResourceHelper.GetString(
                    "ProgressReport_NoEligibleBuildingsMessage",
                    "No buildings match the selected duration."),
                ResourceHelper.GetString(
                    "ProgressReport_NoEligibleBuildingsTitle",
                    "No buildings"));
            return;
        }

        var projectName = SanitizeFileName(_project.Name, "Projet");
        var date = DateTime.Now.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var path = interaction.SelectSavePath(
            ResourceHelper.GetString(
                "ProgressReport_SaveDialogTitle",
                "Export work progress report"),
            ResourceHelper.GetString(
                "ProgressReport_SaveDialogFilter",
                "Excel files (*.xlsx)|*.xlsx"),
            $"RAPPORT AVANCEMENT DE TRAVAUX {projectName} {date}.xlsx");
        if (path == null)
        {
            return;
        }

        await ExportAsync(
            report,
            path,
            "ProgressReport_ExportFailedFormat",
            "Failed to export the progress report:\n{0}");
    }

    public async Task GenerateRemainingCostReportAsync(
        RemainingCostReportType reportType)
    {
        if (!EnsureProject())
        {
            return;
        }

        var data = await dispatcher.Send(
            new GetProjectOperationsReportDataQuery(_project!.Id));
        if (data == null || data.BuildingTypes.Count == 0)
        {
            interaction.Information(
                ResourceHelper.GetString(
                    "RemainingCostReport_NoBuildingTypesMessage",
                    "This project has no buildings with a building type. Nothing to export."),
                ResourceHelper.GetString(
                    "RemainingCostReport_NoBuildingTypesTitle",
                    "No building types"));
            return;
        }
        if (!data.Buildings.Any(building => building.BuildingTypeId.HasValue))
        {
            interaction.Information(
                ResourceHelper.GetString(
                    "RemainingCostReport_NoBuildingsMessage",
                    "There are no buildings to export."),
                ResourceHelper.GetString(
                    "RemainingCostReport_NoBuildingsTitle",
                    "No buildings"));
            return;
        }

        var suffix = ResourceHelper.GetString(
            reportType == RemainingCostReportType.Labor
                ? "RemainingCostReport_TitleLaborSuffix"
                : "RemainingCostReport_TitleMaterialSuffix",
            reportType == RemainingCostReportType.Labor
                ? "Main d'oeuvre"
                : "Matériaux");
        var title = string.IsNullOrWhiteSpace(_project.Name)
            ? suffix
            : $"{_project.Name} - {suffix}";
        var report = OperationsReportBuilder.BuildRemainingCosts(
            data,
            reportType == RemainingCostReportType.Labor
                ? RemainingCostKind.Labor
                : RemainingCostKind.Material,
            title);
        var path = interaction.SelectSavePath(
            reportType == RemainingCostReportType.Labor
                ? ResourceHelper.GetString(
                    "RemainingCostReport_SaveDialogTitleLabor",
                    "Export remaining labor costs report")
                : ResourceHelper.GetString(
                    "RemainingCostReport_SaveDialogTitleMaterial",
                    "Export remaining material costs report"),
            ResourceHelper.GetString(
                "RemainingCostReport_SaveDialogFilter",
                "Excel files (*.xlsx)|*.xlsx"),
            $"{SanitizeFileName(title, "Rapport")}.xlsx");
        if (path == null)
        {
            return;
        }

        await ExportAsync(
            report,
            path,
            "RemainingCostReport_ExportFailedFormat",
            "Failed to export the remaining costs report:\n{0}");
    }

    public async Task ExportSubStagesAsync(int? stageId)
    {
        if (!stageId.HasValue)
        {
            ShowNoStage();
            return;
        }
        var stage = await dispatcher.Send(
            new GetStageOperationsReportDataQuery(stageId.Value));
        if (stage == null)
        {
            ShowNoStage();
            return;
        }
        if (stage.SubStages.Count == 0)
        {
            interaction.Information(
                ResourceHelper.GetString(
                    "OperationsView_NoSubStagesToExport",
                    "There are no sub-stages to export."),
                ResourceHelper.GetString(
                    "OperationsView_NoSubStageTitle",
                    "No sub-stage"));
            return;
        }

        var path = interaction.SelectSavePath(
            ResourceHelper.GetString(
                "OperationsView_ExportSubStagesTitle",
                "Export sub-stages"),
            ResourceHelper.GetString(
                "OperationsView_ExportSubStagesFilter",
                "Excel files (*.xlsx)|*.xlsx"),
            $"{SanitizeFileName(_buildingCode, "Lot")}_{SanitizeFileName(stage.Name, "Stage")}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        if (path == null)
        {
            return;
        }

        var report = OperationsReportBuilder.BuildStageSubStages(
            stage,
            _project?.Name ?? string.Empty,
            _buildingCode,
            status => ResourceHelper.GetString(
                $"WorkStatus_{status}",
                status.ToString()));
        await ExportAsync(
            report,
            path,
            "OperationsView_ExportFailedFormat",
            "Failed to export sub-stages:\n{0}");
    }

    public async Task ExportSubStageMaterialsAsync(int? subStageId)
    {
        if (!subStageId.HasValue)
        {
            ShowNoSubStage();
            return;
        }
        var subStage = await dispatcher.Send(
            new GetSubStageOperationsReportDataQuery(subStageId.Value));
        if (subStage == null)
        {
            ShowNoSubStage();
            return;
        }
        if (subStage.MaterialUsages.Count == 0)
        {
            ShowNoMaterials();
            return;
        }

        var path = interaction.SelectSavePath(
            ResourceHelper.GetString(
                "OperationsView_ExportMaterialsTitle",
                "Export materials"),
            ResourceHelper.GetString(
                "OperationsView_ExportMaterialsFilter",
                "Excel files (*.xlsx)|*.xlsx"),
            $"{SanitizeFileName(_buildingCode, "Lot")}_{SanitizeFileName(_stageName, "Stage")}_{SanitizeFileName(subStage.Name, "Sub-stage")}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        if (path == null)
        {
            return;
        }

        await ExportAsync(
            OperationsReportBuilder.BuildSubStageMaterials(
                subStage,
                _project?.Name ?? string.Empty,
                _buildingCode,
                _stageName),
            path,
            "OperationsView_ExportMaterialsFailedFormat",
            "Failed to export materials:\n{0}");
    }

    public async Task ExportStageMaterialsAsync(int? stageId)
    {
        if (!stageId.HasValue)
        {
            ShowNoStage();
            return;
        }
        var stage = await dispatcher.Send(
            new GetStageOperationsReportDataQuery(stageId.Value));
        if (stage == null)
        {
            ShowNoStage();
            return;
        }
        if (!stage.SubStages.Any(item => item.MaterialUsages.Count > 0))
        {
            ShowNoMaterials();
            return;
        }

        var path = interaction.SelectSavePath(
            ResourceHelper.GetString(
                "OperationsView_ExportStageMaterialsTitle",
                "Export stage materials"),
            ResourceHelper.GetString(
                "OperationsView_ExportMaterialsFilter",
                "Excel files (*.xlsx)|*.xlsx"),
            $"{SanitizeFileName(_buildingCode, "Lot")}_{SanitizeFileName(stage.Name, "Stage")}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        if (path == null)
        {
            return;
        }

        await ExportAsync(
            OperationsReportBuilder.BuildStageMaterials(
                stage,
                _project?.Name ?? string.Empty,
                _buildingCode),
            path,
            "OperationsView_ExportMaterialsFailedFormat",
            "Failed to export materials:\n{0}");
    }

    public async Task<bool> ResolvePaymentsAsync()
    {
        if (!EnsureProject())
        {
            return false;
        }
        var path = interaction.SelectSavePath(
            ResourceHelper.GetString(
                "OperationsView_SavePaymentReportTitle",
                "Save payment resolution report"),
            "PDF files (*.pdf)|*.pdf",
            $"Payment_{_project!.Name}_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        if (path == null)
        {
            return false;
        }

        var result = await dispatcher.Send(
            new ResolvePaymentsCommand(_project.Id, _project.Name, path));
        if (result.IsFailure)
        {
            var nothing = result.Error?.Code == "payment.nothing_to_resolve";
            if (nothing)
            {
                interaction.Information(
                    ResourceHelper.GetString(
                        "OperationsView_NoCompletedSubStages",
                        "No completed sub-stages to resolve."),
                    ResourceHelper.GetString(
                        "OperationsView_NothingToDoTitle",
                        "Nothing to do"));
            }
            else
            {
                interaction.Error(
                    result.Error?.Message ?? ResourceHelper.GetString(
                        "OperationsView_MarkPaidFailedFormat",
                        "Payment resolution failed."),
                    ResourceHelper.GetString("Common_ErrorTitle", "Error"));
            }
            return false;
        }

        interaction.Information(
            ResourceHelper.GetString(
                "OperationsView_PaymentResolutionCompleted",
                "Payment resolution completed."),
            ResourceHelper.GetString(
                "OperationsView_PaymentResolutionCompletedTitle",
                "Done"));
        return true;
    }

    private async Task ExportAsync(
        OperationsExcelReport report,
        string path,
        string errorResourceKey,
        string fallbackError)
    {
        var result = await dispatcher.Send(
            new ExportOperationsExcelCommand(report, path));
        if (result.IsFailure)
        {
            interaction.Error(
                string.Format(
                    ResourceHelper.GetString(errorResourceKey, fallbackError),
                    result.Error?.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"));
            return;
        }
        interaction.OpenFile(path);
    }

    private bool EnsureProject()
    {
        if (_project != null)
        {
            return true;
        }
        interaction.Information(
            ResourceHelper.GetString(
                "OperationsView_SelectProjectFirst",
                "Select a project first."),
            ResourceHelper.GetString(
                "OperationsView_NoProjectTitle",
                "No project"));
        return false;
    }

    private void ShowNoStage() =>
        interaction.Information(
            ResourceHelper.GetString(
                "OperationsView_SelectStageFirst",
                "Select a stage first."),
            ResourceHelper.GetString(
                "OperationsView_NoStageTitle",
                "No stage"));

    private void ShowNoSubStage() =>
        interaction.Information(
            ResourceHelper.GetString(
                "OperationsView_SelectSubStageFirst",
                "Select a sub-stage first."),
            ResourceHelper.GetString(
                "OperationsView_NoSubStageTitle",
                "No sub-stage"));

    private void ShowNoMaterials() =>
        interaction.Information(
            ResourceHelper.GetString(
                "OperationsView_NoMaterialsToExport",
                "There are no materials to export."),
            ResourceHelper.GetString(
                "OperationsView_NoMaterialsTitle",
                "No materials"));

    private static string SanitizeFileName(string? input, string fallback)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return fallback;
        }
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(
            input.Select(character => invalid.Contains(character) ? '_' : character)
                .ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
