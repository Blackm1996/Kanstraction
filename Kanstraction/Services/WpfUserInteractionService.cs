using Kanstraction.OnSite.Application.BuildingTypes.Queries.ListBuildingTypesForCreation;
using Kanstraction.OnSite.Application.Materials.Queries.ListAvailableMaterials;
using Kanstraction.OnSite.Application.Stages.Queries.GetStageAdditionOptions;
using Kanstraction.Views;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Kanstraction.Services;

public sealed class WpfUserInteractionService : IUserInteractionService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public void Information(string message, string title) =>
        MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Warning(string message, string title) =>
        MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void Error(string message, string title) =>
        MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title, bool isWarning = false) =>
        MessageBox.Show(
            Owner,
            message,
            title,
            MessageBoxButton.YesNo,
            isWarning ? MessageBoxImage.Warning : MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool ConfirmValueChange(
        string title,
        string message,
        string confirmText,
        string revertText)
    {
        var dialog = new ConfirmValueChangeDialog(title, message, confirmText, revertText)
        {
            Owner = Owner
        };
        return dialog.ShowDialog() == true;
    }

    public string? PromptText(string title, string? initialValue = null)
    {
        var dialog = new PromptTextDialog(title, initialValue) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public BuildingDraft? CreateBuilding(
        IReadOnlyList<BuildingTypeCreationOptionDto> buildingTypes)
    {
        var dialog = new AddBuildingDialog(buildingTypes) { Owner = Owner };
        return dialog.ShowDialog() == true &&
               dialog.BuildingTypeId.HasValue &&
               !string.IsNullOrWhiteSpace(dialog.BuildingCode)
            ? new BuildingDraft(dialog.BuildingTypeId.Value, dialog.BuildingCode.Trim())
            : null;
    }

    public MaterialUsageDraft? CreateMaterialUsage(
        IReadOnlyList<AvailableMaterialDto> materials)
    {
        var dialog = new AddMaterialToSubStageDialog(materials) { Owner = Owner };
        return dialog.ShowDialog() == true && dialog.MaterialId.HasValue
            ? new MaterialUsageDraft(dialog.MaterialId.Value, dialog.Qty)
            : null;
    }

    public SubStageDraft? CreateSubStage(int currentCount)
    {
        var dialog = new AddSubStageDialog(currentCount) { Owner = Owner };
        return dialog.ShowDialog() == true
            ? new SubStageDraft(dialog.SubStageName, dialog.LaborCost, dialog.OrderIndex)
            : null;
    }

    public StageDraft? CreateStage(
        IReadOnlyList<StagePresetOptionDto> presets,
        int currentCount)
    {
        var rows = presets
            .Select(item => new AddStageToBuildingDialog.PresetVm
            {
                Id = item.Id,
                Name = item.Name
            })
            .ToList();
        var dialog = new AddStageToBuildingDialog(rows, currentCount) { Owner = Owner };
        return dialog.ShowDialog() == true && dialog.SelectedPresetId.HasValue
            ? new StageDraft(dialog.SelectedPresetId.Value, dialog.OrderIndex)
            : null;
    }

    public ReportPeriod? SelectReportPeriod()
    {
        var dialog = new ReportDurationDialog { Owner = Owner };
        return dialog.ShowDialog() == true
            ? new ReportPeriod(dialog.StartDate, dialog.EndDate)
            : null;
    }

    public string? SelectSavePath(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SelectOpenPath(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            Multiselect = false,
            CheckFileExists = true
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // The requested export succeeded even if the associated application cannot be opened.
        }
    }
}
