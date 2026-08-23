using Kanstraction.OnSite.Application.Buildings.Commands.CreateBuilding;
using Kanstraction.OnSite.Application.Buildings.Commands.DeleteBuilding;
using Kanstraction.OnSite.Application.Buildings.Commands.StopBuilding;
using Kanstraction.OnSite.Application.Buildings.Queries.ListBuildingsForOperations;
using Kanstraction.OnSite.Application.BuildingTypes.Queries.ListBuildingTypesForCreation;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.OnSite.Application.Materials.Commands.AddMaterialUsage;
using Kanstraction.OnSite.Application.Materials.Commands.DeleteMaterialUsage;
using Kanstraction.OnSite.Application.Materials.Commands.UpdateUsageQuantity;
using Kanstraction.OnSite.Application.Materials.Queries.ListAvailableMaterials;
using Kanstraction.OnSite.Application.Materials.Queries.ListMaterialUsages;
using Kanstraction.OnSite.Application.Operations.Queries.GetBuildingWorkspace;
using Kanstraction.OnSite.Application.Projects.Queries.ListProjects;
using Kanstraction.OnSite.Application.Stages.Commands.AddStageFromPreset;
using Kanstraction.OnSite.Application.Stages.Commands.ChangeStageStatus;
using Kanstraction.OnSite.Application.Stages.Commands.DeleteStage;
using Kanstraction.OnSite.Application.Stages.Queries.GetStageAdditionOptions;
using Kanstraction.OnSite.Application.SubStages.Commands.AddSubStage;
using Kanstraction.OnSite.Application.SubStages.Commands.ChangeSubStageStatus;
using Kanstraction.OnSite.Application.SubStages.Commands.DeleteSubStage;
using Kanstraction.OnSite.Application.SubStages.Commands.UpdateLaborCost;
using Kanstraction.Services;
using Kanstraction.ViewModels.OnSite.Operations;
using System.Globalization;

namespace Kanstraction.ViewModels.OnSite;

public enum OperationsRefreshPlan
{
    None,
    Materials,
    Workspace
}

/// <summary>
/// Coordinates the On-Site operations screen. All application requests and
/// editable screen state live here; the WPF view only applies visual behavior.
/// </summary>
public sealed class OperationsWorkspaceViewModel : ObservableObject
{
    private readonly IOnSiteChangeFeed _changeFeed;
    private readonly IApplicationDispatcher? _dispatcher;
    private readonly IUserInteractionService? _interaction;
    private long _materialsRevision;
    private long _buildingTypesRevision;
    private long _projectWorkRevision;
    private long _allDataRevision;
    private int _projectLoadVersion;
    private int _buildingSelectionVersion;
    private int _stageSelectionVersion;
    private int _subStageSelectionVersion;
    private ProjectListItemDto? _currentProject;
    private string _breadcrumb;
    private bool _isBusy;
    private bool _suppressSelectionNotifications;

    public OperationsWorkspaceViewModel(
        IOnSiteChangeFeed changeFeed,
        IApplicationDispatcher dispatcher,
        IUserInteractionService interaction)
    {
        _changeFeed = changeFeed;
        _dispatcher = dispatcher;
        _interaction = interaction;
        _breadcrumb = SelectProjectPrompt;
        Reports = new OperationsReportsViewModel(dispatcher, interaction);
        CaptureCurrentRevisions();
    }

    // Used by isolated refresh-policy tests.
    public OperationsWorkspaceViewModel(IOnSiteChangeFeed changeFeed)
    {
        _changeFeed = changeFeed;
        _breadcrumb = SelectProjectPrompt;
        Reports = null!;
        CaptureCurrentRevisions();
    }

    public BuildingsPanelViewModel Buildings { get; } = new();
    public StagesPanelViewModel Stages { get; } = new();
    public SubStagesPanelViewModel SubStages { get; } = new();
    public MaterialUsagesPanelViewModel Materials { get; } = new();
    public OperationsReportsViewModel Reports { get; }

    public ProjectListItemDto? CurrentProject
    {
        get => _currentProject;
        private set
        {
            if (SetProperty(ref _currentProject, value))
            {
                Reports.SetProject(value);
            }
        }
    }

    public string Breadcrumb
    {
        get => _breadcrumb;
        private set => SetProperty(ref _breadcrumb, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool SuppressSelectionNotifications
    {
        get => _suppressSelectionNotifications;
        private set => SetProperty(ref _suppressSelectionNotifications, value);
    }

    public async Task ShowProjectAsync(ProjectListItemDto project)
    {
        EnsureRuntimeServices();
        var version = BeginProjectLoad();
        CurrentProject = project;
        Breadcrumb = project.Name;
        ClearWorkSelection();

        await ReloadBuildingsAsync(projectLoadVersion: version);
        if (!IsCurrentProjectLoad(version) || CurrentProject?.Id != project.Id)
        {
            return;
        }

        if (Buildings.SelectedBuilding is { } building)
        {
            await SelectBuildingAsync(building);
        }
        CaptureCurrentRevisions();
    }

    public async Task ActivateAsync()
    {
        EnsureRuntimeServices();
        if (CurrentProject == null)
        {
            CaptureCurrentRevisions();
            return;
        }

        var selectedBuildingId = Buildings.SelectedBuilding?.Id;
        var selectedStageId = Stages.SelectedStage?.Id;
        var selectedSubStageId = SubStages.SelectedSubStage?.Id;

        switch (GetRefreshPlan())
        {
            case OperationsRefreshPlan.Workspace:
                await ReloadBuildingsAsync(selectedBuildingId, selectedStageId);
                if (Buildings.SelectedBuilding is { } building)
                {
                    await SelectBuildingAsync(building, selectedStageId, selectedSubStageId);
                }
                break;

            case OperationsRefreshPlan.Materials:
                if (SubStages.SelectedSubStage is { } subStage)
                {
                    await SelectSubStageAsync(subStage);
                }
                break;
        }

        CaptureCurrentRevisions();
    }

    public void ClearProject()
    {
        InvalidatePendingRequests();
        CurrentProject = null;
        Breadcrumb = SelectProjectPrompt;
        Buildings.Clear();
        ClearWorkSelection();
    }

    public async Task SelectBuildingAsync(
        BuildingRowViewModel? building,
        int? preferredStageId = null,
        int? preferredSubStageId = null)
    {
        if (building == null)
        {
            ClearWorkSelection();
            return;
        }

        Buildings.SelectedBuilding = building;
        Reports.SetSelection(building.Code, null, null);
        Breadcrumb = $"{CurrentProject?.Name} > {building.Code}";
        var version = BeginBuildingSelection();
        await ReloadWorkspaceAsync(
            building.Id,
            preferredStageId,
            preferredSubStageId,
            version);
    }

    public async Task SelectStageAsync(StageRowViewModel? stage)
    {
        if (stage == null || Buildings.SelectedBuilding == null)
        {
            SubStages.Clear();
            Materials.Clear();
            return;
        }

        Stages.SelectedStage = stage;
        Reports.SetSelection(
            Buildings.SelectedBuilding.Code,
            stage.Name,
            null);
        var version = BeginStageSelection();
        var workspace = await Dispatcher.Send(
            new GetBuildingWorkspaceQuery(Buildings.SelectedBuilding.Id));
        if (!IsCurrentStageSelection(version) ||
            Stages.SelectedStage?.Id != stage.Id)
        {
            return;
        }

        var subStages = workspace?.Stages
            .FirstOrDefault(item => item.Id == stage.Id)?
            .SubStages
            .Select(ToSubStageRow) ?? [];
        SuppressSelectionNotifications = true;
        try
        {
            SubStages.ReplaceItems(subStages);
        }
        finally
        {
            SuppressSelectionNotifications = false;
        }
        if (SubStages.SelectedSubStage is { } selected)
        {
            await SelectSubStageAsync(selected);
        }
        else
        {
            Materials.Clear();
        }
    }

    public async Task SelectSubStageAsync(SubStageRowViewModel? subStage)
    {
        SubStages.SelectedSubStage = subStage;
        Reports.SetSelection(
            Buildings.SelectedBuilding?.Code,
            Stages.SelectedStage?.Name,
            subStage?.Name);
        if (subStage == null)
        {
            Materials.Clear();
            return;
        }

        var version = BeginSubStageSelection();
        var freezePrices =
            subStage.Status is WorkStatusDto.Finished or WorkStatusDto.Paid;
        var usages = await Dispatcher.Send(
            new ListMaterialUsagesQuery(subStage.Id, freezePrices));
        if (!IsCurrentSubStageSelection(version) ||
            SubStages.SelectedSubStage?.Id != subStage.Id)
        {
            return;
        }

        Materials.ReplaceItems(usages.Select(item => new MaterialUsageRowViewModel
        {
            Id = item.Id,
            SubStageId = item.SubStageId,
            MaterialName = item.MaterialName,
            CategoryName = item.CategoryName,
            Unit = item.Unit,
            Qty = item.Quantity,
            UsageDate = item.UsageDate,
            DisplayUnitPrice = item.DisplayUnitPrice
        }));
    }

    public async Task AddBuildingAsync()
    {
        if (CurrentProject == null)
        {
            Interaction.Warning(
                ResourceHelper.GetString("OperationsView_SelectProjectFirst", "Select a project first."),
                ResourceHelper.GetString("OperationsView_NoProjectTitle", "No project"));
            return;
        }

        var types = await Dispatcher.Send(new ListBuildingTypesForCreationQuery());
        var draft = Interaction.CreateBuilding(types);
        if (draft == null)
        {
            return;
        }

        var result = await Dispatcher.Send(
            new CreateBuildingCommand(CurrentProject.Id, draft.BuildingTypeId, draft.Code));
        if (result.IsFailure || result.Value == null)
        {
            ShowApplicationError(result.Error?.Message);
            return;
        }

        await ReloadBuildingsAsync(result.Value.BuildingId);
        if (Buildings.SelectedBuilding is { } selected)
        {
            await SelectBuildingAsync(selected);
        }
    }

    public async Task StopBuildingAsync(BuildingRowViewModel? building)
    {
        if (building == null ||
            !Interaction.Confirm(
                ResourceHelper.GetString(
                    "OperationsView_StopBuildingConfirmMessage",
                    "Stop this building? All unfinished/unpaid stages and sub-stages will be marked Stopped."),
                ResourceHelper.GetString(
                    "OperationsView_StopBuildingConfirmTitle",
                    "Confirm stop")))
        {
            return;
        }

        var result = await Dispatcher.Send(new StopBuildingCommand(building.Id));
        if (result.IsFailure || result.Value == null)
        {
            ShowApplicationError(result.Error?.Message);
            return;
        }

        await Reports.ExportCompensationAsync(result.Value);
        await ReloadBuildingsAsync(building.Id);
        if (Buildings.SelectedBuilding is { } selected)
        {
            await SelectBuildingAsync(selected);
        }
    }

    public async Task DeleteBuildingAsync(BuildingRowViewModel? building)
    {
        if (building == null)
        {
            return;
        }
        if (building.HasPaidItems)
        {
            Interaction.Information(
                ResourceHelper.GetString(
                    "OperationsView_DeleteBuildingPaidMessage",
                    "This building has paid items and cannot be deleted."),
                ResourceHelper.GetString(
                    "OperationsView_DeleteBuildingTitle",
                    "Delete building"));
            return;
        }
        if (!Interaction.Confirm(
                string.Format(
                    ResourceHelper.GetString(
                        "OperationsView_DeleteBuildingConfirmFormat",
                        "Delete building '{0}'? All stages and sub-stages will be removed."),
                    building.Code),
                ResourceHelper.GetString(
                    "OperationsView_DeleteBuildingTitle",
                    "Delete building"),
                isWarning: true))
        {
            return;
        }

        var result = await Dispatcher.Send(new DeleteBuildingCommand(building.Id));
        if (result.IsFailure)
        {
            ShowApplicationError(result.Error?.Message);
            return;
        }

        await ReloadBuildingsAsync();
        if (Buildings.SelectedBuilding is { } selected)
        {
            await SelectBuildingAsync(selected);
        }
        else
        {
            Breadcrumb = CurrentProject?.Name ?? SelectProjectPrompt;
            ClearWorkSelection();
        }
    }

    public async Task ChangeStageStatusAsync(
        StageRowViewModel? stage,
        WorkStatusDto status)
    {
        if (stage == null)
        {
            return;
        }
        var result = await Dispatcher.Send(
            new ChangeStageStatusCommand(stage.Id, status));
        if (result.IsFailure || result.Value == null)
        {
            ShowApplicationError(result.Error?.Message);
            return;
        }
        await ReloadBuildingsAndWorkspaceAsync(
            result.Value.BuildingId,
            result.Value.StageId);
    }

    public async Task AddStageAsync()
    {
        var building = Buildings.SelectedBuilding;
        if (building == null)
        {
            return;
        }
        var options = await Dispatcher.Send(
            new GetStageAdditionOptionsQuery(building.Id));
        if (options == null || options.AvailablePresets.Count == 0)
        {
            Interaction.Information(
                ResourceHelper.GetString(
                    "OperationsView_NoPresetToAddMessage",
                    "All active stage presets are already in this building."),
                ResourceHelper.GetString(
                    "OperationsView_NoPresetTitle",
                    "No preset"));
            return;
        }

        var draft = Interaction.CreateStage(
            options.AvailablePresets,
            options.CurrentStageCount);
        if (draft == null)
        {
            return;
        }

        var result = await Dispatcher.Send(new AddStageFromPresetCommand(
            building.Id,
            options.BuildingTypeId,
            draft.StagePresetId,
            draft.OrderIndex));
        if (result.IsFailure || result.Value == null)
        {
            ShowApplicationError(result.Error?.Message);
            return;
        }
        await ReloadBuildingsAndWorkspaceAsync(building.Id, result.Value.StageId);
    }

    public async Task DeleteStageAsync(StageRowViewModel? stage)
    {
        if (stage == null ||
            !Interaction.Confirm(
                string.Format(
                    ResourceHelper.GetString(
                        "OperationsView_DeleteStageConfirmFormat",
                        "Delete stage '{0}'?"),
                    stage.Name),
                ResourceHelper.GetString(
                    "OperationsView_DeleteStageTitle",
                    "Delete stage"),
                isWarning: true))
        {
            return;
        }

        var result = await Dispatcher.Send(new DeleteStageCommand(stage.Id));
        if (result.IsFailure || result.Value == null)
        {
            ShowApplicationError(result.Error?.Message);
            return;
        }
        await ReloadBuildingsAndWorkspaceAsync(result.Value.BuildingId);
    }

    public async Task AddSubStageAsync()
    {
        var stage = Stages.SelectedStage;
        if (stage == null)
        {
            return;
        }
        var draft = Interaction.CreateSubStage(SubStages.Items.Count);
        if (draft == null)
        {
            return;
        }

        var result = await Dispatcher.Send(new AddSubStageCommand(
            stage.Id,
            draft.Name,
            draft.OrderIndex,
            draft.LaborCost));
        if (result.IsFailure || result.Value == null)
        {
            ShowApplicationError(result.Error?.Message);
            return;
        }

        await ReloadWorkspaceAsync(
            result.Value.BuildingId,
            result.Value.StageId,
            result.Value.SubStageId);
    }

    public async Task DeleteSubStageAsync(SubStageRowViewModel? subStage)
    {
        if (subStage == null ||
            !Interaction.Confirm(
                string.Format(
                    ResourceHelper.GetString(
                        "OperationsView_DeleteSubStageConfirmFormat",
                        "Delete sub-stage '{0}'?"),
                    subStage.Name),
                ResourceHelper.GetString(
                    "OperationsView_DeleteSubStageTitle",
                    "Delete sub-stage"),
                isWarning: true))
        {
            return;
        }

        var result = await Dispatcher.Send(new DeleteSubStageCommand(subStage.Id));
        if (result.IsFailure || result.Value == null)
        {
            ShowApplicationError(result.Error?.Message);
            return;
        }
        await ReloadBuildingsAndWorkspaceAsync(
            result.Value.BuildingId,
            result.Value.StageId);
    }

    public async Task ChangeSubStageStatusAsync(SubStageStatusAction action)
    {
        var subStage = SubStages.SelectedSubStage;
        if (subStage == null)
        {
            return;
        }
        var result = await Dispatcher.Send(
            new ChangeSubStageStatusCommand(subStage.Id, action));
        if (result.IsFailure || result.Value == null)
        {
            ShowApplicationError(result.Error?.Message);
            return;
        }
        await ReloadBuildingsAndWorkspaceAsync(
            result.Value.BuildingId,
            result.Value.StageId);
    }

    public async Task<bool> UpdateLaborCostAsync(
        SubStageRowViewModel subStage,
        decimal originalValue)
    {
        if (subStage.LaborCost < 0)
        {
            Interaction.Warning(
                ResourceHelper.GetString(
                    "OperationsView_LaborCostNegative",
                    "Labor cost cannot be negative."),
                ResourceHelper.GetString("Common_ValidationTitle", "Validation"));
            subStage.LaborCost = originalValue;
            return false;
        }

        if (subStage.LaborCost == originalValue)
        {
            return true;
        }

        var confirmed = Interaction.ConfirmValueChange(
            ResourceHelper.GetString("OperationsView_ConfirmChangeTitle", "Save"),
            string.Format(
                CultureInfo.CurrentCulture,
                ResourceHelper.GetString(
                    "OperationsView_ConfirmLaborChangeMessage",
                    "Are you sure you want to change the labor from {0} to {1}?"),
                FormatDecimal(originalValue),
                FormatDecimal(subStage.LaborCost)),
            ResourceHelper.GetString("Common_Save", "Save"),
            ResourceHelper.GetString("OperationsView_ReturnToDefault", "Cancel"));
        if (!confirmed)
        {
            subStage.LaborCost = originalValue;
            return false;
        }

        var result = await Dispatcher.Send(
            new UpdateSubStageLaborCostCommand(subStage.Id, subStage.LaborCost));
        if (result.IsFailure)
        {
            subStage.LaborCost = originalValue;
            ShowApplicationError(result.Error?.Message);
            return false;
        }
        SubStages.RecalculateTotal();
        return true;
    }

    public async Task AddMaterialUsageAsync()
    {
        var subStage = SubStages.SelectedSubStage;
        if (subStage == null)
        {
            Interaction.Information(
                ResourceHelper.GetString(
                    "OperationsView_SelectSubStageFirst",
                    "Select a sub-stage first."),
                ResourceHelper.GetString(
                    "OperationsView_NoSubStageTitle",
                    "No sub-stage"));
            return;
        }

        var available = await Dispatcher.Send(
            new ListAvailableMaterialsQuery(subStage.Id));
        var draft = Interaction.CreateMaterialUsage(available);
        if (draft == null)
        {
            return;
        }
        var result = await Dispatcher.Send(new AddMaterialUsageCommand(
            subStage.Id,
            draft.MaterialId,
            draft.Quantity));
        if (result.IsFailure)
        {
            ShowApplicationError(result.Error?.Message);
            return;
        }
        await SelectSubStageAsync(subStage);
    }

    public async Task DeleteMaterialUsageAsync(MaterialUsageRowViewModel? usage)
    {
        if (usage == null ||
            !Interaction.Confirm(
                string.Format(
                    ResourceHelper.GetString(
                        "OperationsView_DeleteMaterialConfirmFormat",
                        "Delete material '{0}'?"),
                    usage.MaterialName),
                ResourceHelper.GetString(
                    "OperationsView_DeleteMaterialTitle",
                    "Delete material"),
                isWarning: true))
        {
            return;
        }

        var result = await Dispatcher.Send(
            new DeleteMaterialUsageCommand(usage.SubStageId, usage.Id));
        if (result.IsFailure)
        {
            ShowApplicationError(result.Error?.Message);
            return;
        }
        await SelectSubStageAsync(SubStages.SelectedSubStage);
    }

    public async Task<bool> UpdateMaterialQuantityAsync(
        MaterialUsageRowViewModel usage,
        decimal originalValue)
    {
        if (usage.Qty < 0)
        {
            Interaction.Warning(
                ResourceHelper.GetString(
                    "OperationsView_QuantityNegative",
                    "Quantity cannot be negative."),
                ResourceHelper.GetString("Common_ValidationTitle", "Validation"));
            usage.Qty = originalValue;
            return false;
        }
        if (usage.Qty == originalValue)
        {
            return true;
        }

        var confirmed = Interaction.ConfirmValueChange(
            ResourceHelper.GetString(
                "OperationsView_ConfirmChangeTitle",
                "Confirm change"),
            string.Format(
                CultureInfo.CurrentCulture,
                ResourceHelper.GetString(
                    "OperationsView_ConfirmMaterialChangeMessage",
                    "Are you sure you want to change the quantity from {0} to {1}?"),
                FormatDecimal(originalValue),
                FormatDecimal(usage.Qty)),
            ResourceHelper.GetString("Common_Save", "Save"),
            ResourceHelper.GetString(
                "OperationsView_ReturnToDefault",
                "Return to default"));
        if (!confirmed)
        {
            usage.Qty = originalValue;
            return false;
        }

        var result = await Dispatcher.Send(new UpdateMaterialUsageQuantityCommand(
            usage.SubStageId,
            usage.Id,
            usage.Qty));
        if (result.IsFailure)
        {
            usage.Qty = originalValue;
            ShowApplicationError(result.Error?.Message);
            return false;
        }
        Materials.RecalculateTotal();
        return true;
    }

    public async Task RefreshAfterPaymentAsync()
    {
        var buildingId = Buildings.SelectedBuilding?.Id;
        var stageId = Stages.SelectedStage?.Id;
        await ReloadBuildingsAsync(buildingId, stageId);
        if (Buildings.SelectedBuilding is { } building)
        {
            await SelectBuildingAsync(building, stageId);
        }
        CaptureCurrentRevisions();
    }

    public OperationsRefreshPlan GetRefreshPlan()
    {
        if (RevisionChanged(OnSiteChangeScope.BuildingTypes, _buildingTypesRevision) ||
            RevisionChanged(OnSiteChangeScope.ProjectWork, _projectWorkRevision) ||
            RevisionChanged(OnSiteChangeScope.AllData, _allDataRevision))
        {
            return OperationsRefreshPlan.Workspace;
        }
        return RevisionChanged(OnSiteChangeScope.Materials, _materialsRevision)
            ? OperationsRefreshPlan.Materials
            : OperationsRefreshPlan.None;
    }

    public void CaptureCurrentRevisions()
    {
        _materialsRevision = Revision(OnSiteChangeScope.Materials);
        _buildingTypesRevision = Revision(OnSiteChangeScope.BuildingTypes);
        _projectWorkRevision = Revision(OnSiteChangeScope.ProjectWork);
        _allDataRevision = Revision(OnSiteChangeScope.AllData);
    }

    public int BeginProjectLoad() => ++_projectLoadVersion;
    public int BeginBuildingSelection() => ++_buildingSelectionVersion;
    public int BeginStageSelection() => ++_stageSelectionVersion;
    public int BeginSubStageSelection() => ++_subStageSelectionVersion;
    public bool IsCurrentProjectLoad(int version) => version == _projectLoadVersion;
    public bool IsCurrentBuildingSelection(int version) => version == _buildingSelectionVersion;
    public bool IsCurrentStageSelection(int version) => version == _stageSelectionVersion;
    public bool IsCurrentSubStageSelection(int version) => version == _subStageSelectionVersion;

    public void InvalidatePendingRequests()
    {
        _projectLoadVersion++;
        _buildingSelectionVersion++;
        _stageSelectionVersion++;
        _subStageSelectionVersion++;
    }

    private async Task ReloadBuildingsAndWorkspaceAsync(
        int buildingId,
        int? stageId = null)
    {
        await ReloadBuildingsAsync(buildingId, stageId);
        if (Buildings.SelectedBuilding is { } building)
        {
            await SelectBuildingAsync(building, stageId);
        }
    }

    private async Task ReloadBuildingsAsync(
        int? preferredBuildingId = null,
        int? preferredStageId = null,
        int? projectLoadVersion = null)
    {
        if (CurrentProject == null)
        {
            Buildings.Clear();
            return;
        }

        IsBusy = true;
        try
        {
            var projectId = CurrentProject.Id;
            var buildings = await Dispatcher.Send(
                new ListBuildingsForOperationsQuery(projectId));
            if ((projectLoadVersion.HasValue &&
                 !IsCurrentProjectLoad(projectLoadVersion.Value)) ||
                CurrentProject?.Id != projectId)
            {
                return;
            }

            SuppressSelectionNotifications = true;
            try
            {
                Buildings.ReplaceItems(
                    buildings.Select(item => new BuildingRowViewModel
                    {
                        Id = item.Id,
                        Code = item.Code,
                        TypeName = item.TypeName,
                        Status = item.Status,
                        ProgressPercent = item.ProgressPercent,
                        CurrentStageName = item.CurrentStageName,
                        HasPaidItems = item.HasPaidItems
                    }),
                    preferredBuildingId);
            }
            finally
            {
                SuppressSelectionNotifications = false;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadWorkspaceAsync(
        int buildingId,
        int? preferredStageId = null,
        int? preferredSubStageId = null,
        int? buildingSelectionVersion = null)
    {
        var workspace = await Dispatcher.Send(
            new GetBuildingWorkspaceQuery(buildingId));
        if (buildingSelectionVersion.HasValue &&
            (!IsCurrentBuildingSelection(buildingSelectionVersion.Value) ||
             Buildings.SelectedBuilding?.Id != buildingId))
        {
            return;
        }

        var stages = workspace?.Stages.Select(item => new StageRowViewModel
        {
            Id = item.Id,
            Name = item.Name,
            Status = item.Status,
            ProgressPercent = item.ProgressPercent,
            OngoingSubStageName = item.OngoingSubStageName,
            PaidDate = item.PaidDate
        }) ?? [];
        SuppressSelectionNotifications = true;
        try
        {
            Stages.ReplaceItems(stages, preferredStageId);
        }
        finally
        {
            SuppressSelectionNotifications = false;
        }

        if (Stages.SelectedStage == null)
        {
            SubStages.Clear();
            Materials.Clear();
            return;
        }

        Reports.SetSelection(
            Buildings.SelectedBuilding?.Code,
            Stages.SelectedStage.Name,
            null);
        var subStages = workspace?.Stages
            .FirstOrDefault(item => item.Id == Stages.SelectedStage.Id)?
            .SubStages
            .Select(ToSubStageRow) ?? [];
        SuppressSelectionNotifications = true;
        try
        {
            SubStages.ReplaceItems(subStages, preferredSubStageId);
        }
        finally
        {
            SuppressSelectionNotifications = false;
        }
        if (SubStages.SelectedSubStage is { } selected)
        {
            await SelectSubStageAsync(selected);
        }
        else
        {
            Materials.Clear();
        }
    }

    private void ClearWorkSelection()
    {
        Stages.Clear();
        SubStages.Clear();
        Materials.Clear();
        Reports.SetSelection(null, null, null);
    }

    private static SubStageRowViewModel ToSubStageRow(
        OperationsSubStageDto item) => new()
        {
            Id = item.Id,
            StageId = item.StageId,
            Name = item.Name,
            OrderIndex = item.OrderIndex,
            Status = item.Status,
            StartDate = item.StartDate,
            EndDate = item.EndDate,
            LaborCost = item.LaborCost
        };

    private void ShowApplicationError(string? message) =>
        Interaction.Warning(
            message ?? ResourceHelper.GetString(
                "Common_UnknownError",
                "The operation could not be completed."),
            ResourceHelper.GetString("OperationsView_RuleTitle", "Rule"));

    private void EnsureRuntimeServices()
    {
        _ = Dispatcher;
        _ = Interaction;
    }

    private IApplicationDispatcher Dispatcher =>
        _dispatcher ?? throw new InvalidOperationException(
            "This view model instance was created for policy testing only.");

    private IUserInteractionService Interaction =>
        _interaction ?? throw new InvalidOperationException(
            "This view model instance was created for policy testing only.");

    private long Revision(OnSiteChangeScope scope) => _changeFeed.GetRevision(scope);
    private bool RevisionChanged(OnSiteChangeScope scope, long seen) => Revision(scope) != seen;
    private static string FormatDecimal(decimal value) =>
        value.ToString("0.##", CultureInfo.CurrentCulture);
    private static string SelectProjectPrompt =>
        ResourceHelper.GetString(
            "OperationsView_SelectProjectPrompt",
            "Select a project");
}
