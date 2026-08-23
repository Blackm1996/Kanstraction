using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Projects.Queries.ListProjects;
using Kanstraction.OnSite.Application.SubStages.Commands.ChangeSubStageStatus;
using Kanstraction.ViewModels.OnSite;
using Kanstraction.ViewModels.OnSite.Operations;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace Kanstraction.Views;

public partial class OperationsView : UserControl
{
    private readonly OperationsWorkspaceViewModel _viewModel;
    private SubStageRowViewModel? _editingSubStage;
    private decimal? _originalLaborCost;
    private MaterialUsageRowViewModel? _editingMaterialUsage;
    private decimal? _originalQuantity;

    public OperationsView(OperationsWorkspaceViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public Task ShowProject(ProjectListItemDto project) =>
        RunAsync(() => _viewModel.ShowProjectAsync(project));

    public Task ActivateAsync() =>
        RunAsync(_viewModel.ActivateAsync);

    public void ClearProject() =>
        _viewModel.ClearProject();

    public void MarkCurrentRevisionsAsSeen() =>
        _viewModel.CaptureCurrentRevisions();

    private async void BuildingsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_viewModel.SuppressSelectionNotifications)
        {
            await RunAsync(() =>
                _viewModel.SelectBuildingAsync(
                    BuildingsGrid.SelectedItem as BuildingRowViewModel));
        }
    }

    private async void StagesGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_viewModel.SuppressSelectionNotifications)
        {
            await RunAsync(() =>
                _viewModel.SelectStageAsync(
                    StagesGrid.SelectedItem as StageRowViewModel));
        }
    }

    private async void SubStagesGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_viewModel.SuppressSelectionNotifications)
        {
            await RunAsync(() =>
                _viewModel.SelectSubStageAsync(
                    SubStagesGrid.SelectedItem as SubStageRowViewModel));
        }
    }

    private void ChangeStageStatus_Click(object sender, RoutedEventArgs e) =>
        OpenContextMenu(sender, placeBelow: true);

    private async void StageStatusMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem
            {
                Tag: WorkStatusDto status,
                DataContext: StageRowViewModel stage
            })
        {
            await RunAsync(() =>
                _viewModel.ChangeStageStatusAsync(stage, status));
        }
    }

    private void SubStagesGrid_PreparingCellForEdit(
        object sender,
        DataGridPreparingCellForEditEventArgs e)
    {
        if (IsColumn(e.Column, nameof(SubStageRowViewModel.LaborCost)) &&
            e.Row.Item is SubStageRowViewModel subStage)
        {
            _editingSubStage = subStage;
            _originalLaborCost = subStage.LaborCost;
        }
        else
        {
            _editingSubStage = null;
            _originalLaborCost = null;
        }
    }

    private async void SubStagesGrid_CellEditEnding(
        object sender,
        DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit ||
            !IsColumn(e.Column, nameof(SubStageRowViewModel.LaborCost)) ||
            e.Row.Item is not SubStageRowViewModel subStage)
        {
            ClearLaborEdit();
            return;
        }

        var original = ReferenceEquals(_editingSubStage, subStage) &&
                       _originalLaborCost.HasValue
            ? _originalLaborCost.Value
            : subStage.LaborCost;
        var accepted = await RunAsync(
            () => _viewModel.UpdateLaborCostAsync(subStage, original),
            fallback: false);
        if (!accepted && e.EditingElement is TextBox textBox)
        {
            textBox.Text = FormatDecimal(subStage.LaborCost);
        }
        ClearLaborEdit();
    }

    private void MaterialsGrid_PreparingCellForEdit(
        object sender,
        DataGridPreparingCellForEditEventArgs e)
    {
        if (IsColumn(e.Column, nameof(MaterialUsageRowViewModel.Qty)) &&
            e.Row.Item is MaterialUsageRowViewModel usage)
        {
            _editingMaterialUsage = usage;
            _originalQuantity = usage.Qty;
        }
        else
        {
            _editingMaterialUsage = null;
            _originalQuantity = null;
        }
    }

    private async void MaterialsGrid_CellEditEnding(
        object sender,
        DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit ||
            !IsColumn(e.Column, nameof(MaterialUsageRowViewModel.Qty)) ||
            e.Row.Item is not MaterialUsageRowViewModel usage)
        {
            ClearMaterialEdit();
            return;
        }

        if (e.EditingElement is TextBox editor &&
            decimal.TryParse(
                editor.Text,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var parsed))
        {
            usage.Qty = parsed;
        }

        var original = ReferenceEquals(_editingMaterialUsage, usage) &&
                       _originalQuantity.HasValue
            ? _originalQuantity.Value
            : usage.Qty;
        var accepted = await RunAsync(
            () => _viewModel.UpdateMaterialQuantityAsync(usage, original),
            fallback: false);
        if (!accepted && e.EditingElement is TextBox textBox)
        {
            textBox.Text = FormatDecimal(usage.Qty);
        }
        ClearMaterialEdit();
    }

    private async void AddBuilding_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.AddBuildingAsync);

    private async void StopBuilding_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() =>
            _viewModel.StopBuildingAsync(
                (sender as FrameworkElement)?.DataContext as BuildingRowViewModel));

    private async void DeleteBuilding_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() =>
            _viewModel.DeleteBuildingAsync(
                (sender as FrameworkElement)?.DataContext as BuildingRowViewModel));

    private async void AddStageToBuilding_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.AddStageAsync);

    private async void DeleteStage_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() =>
            _viewModel.DeleteStageAsync(
                (sender as FrameworkElement)?.DataContext as StageRowViewModel));

    private async void AddSubStage_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.AddSubStageAsync);

    private async void DeleteSubStageInstance_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() =>
            _viewModel.DeleteSubStageAsync(
                (sender as FrameworkElement)?.DataContext as SubStageRowViewModel));

    private async void StartSubStage_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() =>
            _viewModel.ChangeSubStageStatusAsync(SubStageStatusAction.Start));

    private async void FinishSubStage_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() =>
            _viewModel.ChangeSubStageStatusAsync(SubStageStatusAction.Finish));

    private async void ResetSubStage_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() =>
            _viewModel.ChangeSubStageStatusAsync(SubStageStatusAction.Reset));

    private async void AddMaterialToSubStage_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.AddMaterialUsageAsync);

    private async void DeleteMaterialUsage_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() =>
            _viewModel.DeleteMaterialUsageAsync(
                (sender as FrameworkElement)?.DataContext as MaterialUsageRowViewModel));

    private void ReportsMenuButton_Click(object sender, RoutedEventArgs e) =>
        OpenContextMenu(sender);

    private void ExportSubStagesMenuButton_Click(object sender, RoutedEventArgs e) =>
        OpenContextMenu(sender);

    private async void GenerateProgressReport_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.Reports.GenerateProgressReportAsync);

    private async void GenerateRemainingLaborReport_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() =>
            _viewModel.Reports.GenerateRemainingCostReportAsync(
                RemainingCostReportType.Labor));

    private async void GenerateRemainingMaterialReport_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() =>
            _viewModel.Reports.GenerateRemainingCostReportAsync(
                RemainingCostReportType.Material));

    private async void ExportSubStages_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() =>
            _viewModel.Reports.ExportSubStagesAsync(
                _viewModel.Stages.SelectedStage?.Id));

    private async void ExportMaterials_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() =>
            _viewModel.Reports.ExportSubStageMaterialsAsync(
                _viewModel.SubStages.SelectedSubStage?.Id));

    private async void ExportStageMaterials_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() =>
            _viewModel.Reports.ExportStageMaterialsAsync(
                _viewModel.Stages.SelectedStage?.Id));

    private async void ResolvePayment_Click(object sender, RoutedEventArgs e)
    {
        var resolved = await RunAsync(
            _viewModel.Reports.ResolvePaymentsAsync,
            fallback: false);
        if (resolved)
        {
            await RunAsync(_viewModel.RefreshAfterPaymentAsync);
        }
    }

    private static bool IsColumn(DataGridColumn column, string propertyName) =>
        column is DataGridTextColumn
        {
            Binding: Binding { Path.Path: var path }
        } && path == propertyName;

    private static void OpenContextMenu(object sender, bool placeBelow = false)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }
        menu.PlacementTarget = button;
        if (placeBelow)
        {
            menu.Placement = PlacementMode.Bottom;
        }
        menu.IsOpen = true;
    }

    private static async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ShowUnexpectedError(exception);
        }
    }

    private static async Task<T> RunAsync<T>(
        Func<Task<T>> action,
        T fallback)
    {
        try
        {
            return await action();
        }
        catch (Exception exception)
        {
            ShowUnexpectedError(exception);
            return fallback;
        }
    }

    private static void ShowUnexpectedError(Exception exception) =>
        MessageBox.Show(
            string.Format(
                ResourceHelper.GetString(
                    "Common_UnexpectedErrorFormat",
                    "The operation failed:\n{0}"),
                exception.Message),
            ResourceHelper.GetString("Common_ErrorTitle", "Error"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);

    private void ClearLaborEdit()
    {
        _editingSubStage = null;
        _originalLaborCost = null;
    }

    private void ClearMaterialEdit()
    {
        _editingMaterialUsage = null;
        _originalQuantity = null;
    }

    private static string FormatDecimal(decimal value) =>
        value.ToString("0.##", CultureInfo.CurrentCulture);
}
