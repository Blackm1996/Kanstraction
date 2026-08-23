using Kanstraction.OnSite.Application.BuildingTypes.Queries.ListBuildingTypesForAdmin;
using Kanstraction.OnSite.Application.Materials.Queries.ListAvailableMaterials;
using Kanstraction.OnSite.Application.Materials.Queries.ListMaterialsForAdmin;
using Kanstraction.ViewModels.OnSite;
using Kanstraction.ViewModels.OnSite.Administration;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Kanstraction.Views;

public partial class AdminHubView : UserControl
{
    private readonly AdministrationWorkspaceViewModel _workspace;
    private readonly StagePresetDesignerViewModel _presetDesigner;
    private readonly SemaphoreSlim _tabLoadGate = new(1, 1);

    public AdminHubView(
        AdministrationWorkspaceViewModel workspace,
        MaterialCatalogViewModel materials,
        BuildingTypeDesignerViewModel buildingTypes,
        StagePresetCatalogViewModel presets,
        StagePresetDesignerViewModel presetDesigner)
    {
        _workspace = workspace;
        _presetDesigner = presetDesigner;
        Materials = materials;
        BuildingTypes = buildingTypes;
        Presets = presets;
        DataContext = this;

        InitializeComponent();
        Loaded += OnLoaded;
    }

    public MaterialCatalogViewModel Materials { get; }
    public BuildingTypeDesignerViewModel BuildingTypes { get; }
    public StagePresetCatalogViewModel Presets { get; }

    public async Task ActivateAsync()
    {
        ApplyRevisionInvalidations();
        if (_workspace.IsViewLoaded)
        {
            await EnsureSelectedTabLoadedAsync();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _workspace.IsViewLoaded = true;
        ConfigurePresetDesigner();
        HookPresetDesignerEvents();
        ApplyRevisionInvalidations();
        await RunAdminAsync(() => EnsureSelectedTabLoadedAsync());
    }

    private async void AdminTabs_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (e.Source == AdminTabs && _workspace.IsViewLoaded)
        {
            await RunAdminAsync(() => EnsureSelectedTabLoadedAsync());
        }
    }

    private async Task EnsureSelectedTabLoadedAsync(bool forceRefresh = false)
    {
        await _tabLoadGate.WaitAsync();
        try
        {
            ApplyRevisionInvalidations();
            switch (AdminTabs?.SelectedIndex)
            {
                case 0:
                    if (forceRefresh || !_workspace.MaterialsLoaded)
                    {
                        await Materials.LoadAsync();
                        PublishAvailableMaterialsToStageEditor();
                        _workspace.MaterialsLoaded = true;
                        _workspace.CaptureMaterialRevisions();
                    }
                    break;

                case 1:
                    if (forceRefresh || !_workspace.StagePresetsLoaded)
                    {
                        await Presets.LoadAsync();
                        PublishAvailableMaterialsToStageEditor();
                        await BeginNewStagePresetAsync();
                        _workspace.StagePresetsLoaded = true;
                        _workspace.StagePresetsStale = false;
                        _workspace.CaptureStagePresetsRevision();
                    }
                    else if (_workspace.StagePresetsStale)
                    {
                        PublishAvailableMaterialsToStageEditor();
                        _workspace.StagePresetsStale = false;
                    }
                    break;

                case 2:
                    if (forceRefresh || !_workspace.BuildingTypesLoaded)
                    {
                        await BuildingTypes.LoadAsync();
                        _workspace.BuildingTypesLoaded = true;
                        _workspace.BuildingTypesStale = false;
                        _workspace.CaptureBuildingReferenceRevisions();
                    }
                    else if (_workspace.BuildingTypesStale && BuildingTypes.IsDirty)
                    {
                        await BuildingTypes.RefreshReferencesPreservingDraftAsync();
                        _workspace.BuildingTypesStale = false;
                        _workspace.CaptureBuildingReferenceRevisions();
                    }
                    else if (_workspace.BuildingTypesStale)
                    {
                        await BuildingTypes.LoadAsync(beginNew: false);
                        _workspace.BuildingTypesStale = false;
                        _workspace.CaptureBuildingReferenceRevisions();
                    }
                    break;
            }
        }
        finally
        {
            _tabLoadGate.Release();
        }
    }

    public async Task RefreshAfterDatabaseRestoreAsync()
    {
        _workspace.InvalidateAllTabs();
        if (_workspace.IsViewLoaded)
        {
            await EnsureSelectedTabLoadedAsync(forceRefresh: true);
        }
        _workspace.CaptureAllRevisions();
    }

    private void ApplyRevisionInvalidations() =>
        _workspace.ApplyRevisionInvalidations();

    private void ConfigurePresetDesigner() =>
        PresetDesigner?.SetViewModel(_presetDesigner);

    private void HookPresetDesignerEvents()
    {
        if (PresetDesigner == null)
        {
            return;
        }
        PresetDesigner.Saved -= PresetDesignerSaved;
        PresetDesigner.Saved += PresetDesignerSaved;
    }

    private async Task BeginNewStagePresetAsync()
    {
        Presets.BeginNew();
        ConfigurePresetDesigner();
        if (PresetDesigner != null)
        {
            await PresetDesigner.LoadPresetAsync(null);
        }
    }

    private void PublishAvailableMaterialsToStageEditor()
    {
        if (PresetDesigner == null)
        {
            return;
        }
        var references = Materials.AllMaterials
            .OrderBy(material => material.CategoryName)
            .ThenBy(material => material.Name)
            .Select(ToAvailableMaterial)
            .ToList();
        var activeIds = Materials.AllMaterials
            .Where(material => material.IsActive)
            .Select(material => material.Id)
            .ToHashSet();
        PresetDesigner.SetAvailableMaterials(
            references.Where(material => activeIds.Contains(material.Id)),
            references);
    }

    private static AvailableMaterialDto ToAvailableMaterial(
        MaterialListItemDto material) =>
        new(
            material.Id,
            material.Name,
            material.Unit,
            material.CategoryName);

    private async void MaterialsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        await RunAdminAsync(() => Materials.SelectAsync(
            MaterialsList?.SelectedItem as MaterialListItemDto));

    private void NewMaterial_Click(object sender, RoutedEventArgs e) =>
        Materials.BeginNew();

    private async void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        await RunAdminAsync(Materials.AddCategoryAsync);
        MaterialReferencesChanged();
    }

    private async void SaveMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (await RunAdminAsync(() => Materials.SaveAsync(), false))
        {
            MaterialReferencesChanged();
        }
    }

    private async void MatDeactivateButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAdminAsync(Materials.DeactivateAsync);
        MaterialReferencesChanged();
    }

    private async void MatReactivateButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAdminAsync(Materials.ReactivateAsync);
        MaterialReferencesChanged();
    }

    private void CancelMaterial_Click(object sender, RoutedEventArgs e) =>
        Materials.Cancel();

    private void MaterialReferencesChanged()
    {
        PublishAvailableMaterialsToStageEditor();
        _workspace.CaptureMaterialRevisions();
        _workspace.BuildingTypesStale = true;
    }

    private async void PresetSelection_Mvvm(
        object sender,
        SelectionChangedEventArgs e)
    {
        ConfigurePresetDesigner();
        await RunAdminAsync(() =>
            PresetDesigner.LoadPresetAsync(Presets.SelectedPreset?.Id));
    }

    private async void NewPreset_Click(object sender, RoutedEventArgs e) =>
        await RunAdminAsync(BeginNewStagePresetAsync);

    private async void PresetDesignerSaved(object? sender, int presetId)
    {
        var reset = PresetDesigner.ShouldResetAfterSave;
        await RunAdminAsync(() => Presets.LoadAsync(reset ? null : presetId));
        _workspace.StagePresetsLoaded = true;
        _workspace.BuildingTypesStale = true;
        _workspace.CaptureStagePresetsRevision();

        if (reset)
        {
            await RunAdminAsync(BeginNewStagePresetAsync);
        }
        if (AdminTabs?.SelectedIndex == 2)
        {
            await RunAdminAsync(BuildingTypes.RefreshReferencesPreservingDraftAsync);
            _workspace.BuildingTypesStale = false;
        }
    }

    private async void BuildingTypeSelection_Mvvm(
        object sender,
        SelectionChangedEventArgs e) =>
        await RunAdminAsync(() => BuildingTypes.SelectAsync(
            BuildingTypesList?.SelectedItem as BuildingTypeListItemDto));

    private void NewBuildingType_Click(object sender, RoutedEventArgs e) =>
        BuildingTypes.BeginNew();

    private void AddBuildingTypePreset_Mvvm(object sender, RoutedEventArgs e) =>
        BuildingTypes.AddPreset();

    private void AssignedBuildingTypeStageSelection_Mvvm(
        object sender,
        SelectionChangedEventArgs e) =>
        BuildingTypes.SelectedAssignedStage =
            BtAssignedGrid?.SelectedItem as BuildingTypeAssignedStageViewModel;

    private void BuildingTypeSubStageSelection_Mvvm(
        object sender,
        SelectionChangedEventArgs e) =>
        BuildingTypes.SelectedSubStage =
            BtSubStagesPreviewGrid?.SelectedItem as BuildingTypeSubStageViewModel;

    private void MoveBuildingTypeStageUp_Mvvm(object sender, RoutedEventArgs e) =>
        BuildingTypes.MoveAssignedStage(
            (sender as FrameworkElement)?.DataContext as BuildingTypeAssignedStageViewModel,
            -1);

    private void MoveBuildingTypeStageDown_Mvvm(object sender, RoutedEventArgs e) =>
        BuildingTypes.MoveAssignedStage(
            (sender as FrameworkElement)?.DataContext as BuildingTypeAssignedStageViewModel,
            1);

    private void RemoveBuildingTypeStage_Mvvm(object sender, RoutedEventArgs e) =>
        BuildingTypes.RemoveAssignedStage(
            (sender as FrameworkElement)?.DataContext as BuildingTypeAssignedStageViewModel);

    private async void SaveBuildingType_Mvvm(object sender, RoutedEventArgs e)
    {
        if (await RunAdminAsync(() => BuildingTypes.SaveAsync(), false))
        {
            _workspace.BuildingTypesLoaded = true;
            _workspace.BuildingTypesStale = false;
            _workspace.CaptureBuildingTypesRevision();
        }
    }

    private async void DeactivateBuildingType_Mvvm(object sender, RoutedEventArgs e)
    {
        await RunAdminAsync(BuildingTypes.DeactivateAsync);
        _workspace.CaptureBuildingTypesRevision();
    }

    private async void ReactivateBuildingType_Mvvm(object sender, RoutedEventArgs e)
    {
        await RunAdminAsync(BuildingTypes.ReactivateAsync);
        _workspace.CaptureBuildingTypesRevision();
    }

    private async void CancelBuildingType_Mvvm(object sender, RoutedEventArgs e) =>
        await RunAdminAsync(BuildingTypes.CancelAsync);

    private static async Task RunAdminAsync(Func<Task> action)
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

    private static async Task<T> RunAdminAsync<T>(
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
            exception.Message,
            ResourceHelper.GetString("Common_ErrorTitle", "Error"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
}
