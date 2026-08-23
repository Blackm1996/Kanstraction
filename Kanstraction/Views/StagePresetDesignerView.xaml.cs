using Kanstraction.OnSite.Application.Materials.Queries.ListAvailableMaterials;
using Kanstraction.ViewModels.OnSite;
using System.Windows;
using System.Windows.Controls;

namespace Kanstraction.Views;

public partial class StagePresetDesignerView : UserControl
{
    private StagePresetDesignerViewModel _viewModel = new();

    public StagePresetDesignerView()
    {
        DataContext = _viewModel;
        InitializeComponent();
        _viewModel.Saved += ViewModelSaved;
    }

    public bool IsDirty => _viewModel.IsDirty;
    public bool ShouldResetAfterSave => _viewModel.ShouldResetAfterSave;
    public event EventHandler<int>? Saved;

    public void SetViewModel(StagePresetDesignerViewModel viewModel)
    {
        _viewModel.Saved -= ViewModelSaved;
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.Saved += ViewModelSaved;
    }

    private void ViewModelSaved(object? sender, int presetId) =>
        Saved?.Invoke(this, presetId);

    public Task LoadPresetAsync(int? presetId) =>
        _viewModel.LoadPresetAsync(presetId);

    public Task RefreshAvailableMaterialsAsync() =>
        _viewModel.RefreshAvailableMaterialsAsync();

    public void SetAvailableMaterials(
        IEnumerable<AvailableMaterialDto> materials,
        IEnumerable<AvailableMaterialDto>? metadata = null) =>
        _viewModel.SetAvailableMaterials(materials, metadata);

    public void EnterEmptyState() =>
        _viewModel.EnterEmptyState();

    private async void EmptyStateCreateNew_Click(
        object sender,
        RoutedEventArgs e) =>
        await RunAsync(() => _viewModel.LoadPresetAsync(null));

    private void AddSubStage_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddSubStage();
        if (_viewModel.SelectedSubStage is { } selected)
        {
            SubStagesGrid.SelectedItem = selected;
            SubStagesGrid.ScrollIntoView(selected);
            SubStagesGrid.CurrentCell = new DataGridCellInfo(
                selected,
                SubStagesGrid.Columns[1]);
            SubStagesGrid.BeginEdit();
        }
    }

    private void DeleteSubStage_Click(object sender, RoutedEventArgs e) =>
        _viewModel.DeleteSubStage(
            (sender as FrameworkElement)?.DataContext as StagePresetSubStageViewModel);

    private void MoveSubStageUp_Click(object sender, RoutedEventArgs e) =>
        _viewModel.MoveSubStage(
            (sender as FrameworkElement)?.DataContext as StagePresetSubStageViewModel,
            -1);

    private void MoveSubStageDown_Click(object sender, RoutedEventArgs e) =>
        _viewModel.MoveSubStage(
            (sender as FrameworkElement)?.DataContext as StagePresetSubStageViewModel,
            1);

    private void SubStagesGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        _viewModel.SelectedSubStage =
            SubStagesGrid.SelectedItem as StagePresetSubStageViewModel;

    private void AddMaterial_Click(object sender, RoutedEventArgs e) =>
        _viewModel.AddMaterial();

    private void RemoveMaterial_Click(object sender, RoutedEventArgs e) =>
        _viewModel.RemoveMaterial(
            (sender as FrameworkElement)?.DataContext as StagePresetMaterialUsageViewModel);

    private async void Save_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => _viewModel.SaveAsync());

    private async void DeactivatePreset_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.DeactivateAsync);

    private async void ReactivatePreset_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.ReactivateAsync);

    private async void Cancel_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.CancelAsync);

    private static async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
