using Kanstraction.OnSite.Application.BuildingTypes.Commands.SaveBuildingType;
using Kanstraction.OnSite.Application.BuildingTypes.Queries.GetBuildingTypeEditor;
using Kanstraction.OnSite.Application.BuildingTypes.Queries.ListBuildingTypesForAdmin;
using Kanstraction.OnSite.Application.StagePresets.Queries.ListStagePresets;
using Kanstraction.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Kanstraction.ViewModels.OnSite.Administration;

public sealed class BuildingTypeDesignerViewModel : ObservableObject
{
    private readonly IApplicationDispatcher _dispatcher;
    private readonly IUserInteractionService _interaction;
    private readonly List<BuildingTypeListItemDto> _allBuildingTypes = [];
    private readonly List<StagePresetListItemDto> _stagePresetCatalog = [];
    private BuildingTypeEditorWorkspaceDto? _workspace;
    private BuildingTypeListItemDto? _selectedBuildingType;
    private BuildingTypeAssignedStageViewModel? _selectedAssignedStage;
    private BuildingTypeSubStageViewModel? _selectedSubStage;
    private BuildingTypeStageEditorDto? _selectedAvailablePreset;
    private int? _editingId;
    private string _searchText = string.Empty;
    private bool _activeOnly = true;
    private string _name = string.Empty;
    private bool _isActive = true;
    private bool _isDirty;
    private int _selectionVersion;

    public BuildingTypeDesignerViewModel(
        IApplicationDispatcher dispatcher,
        IUserInteractionService interaction)
    {
        _dispatcher = dispatcher;
        _interaction = interaction;
    }

    public ObservableCollection<BuildingTypeListItemDto> BuildingTypes { get; } = [];
    public ObservableCollection<BuildingTypeAssignedStageViewModel> AssignedStages { get; } = [];
    public ObservableCollection<BuildingTypeStageEditorDto> AvailablePresets { get; } = [];

    public BuildingTypeListItemDto? SelectedBuildingType
    {
        get => _selectedBuildingType;
        set => SetProperty(ref _selectedBuildingType, value);
    }

    public BuildingTypeAssignedStageViewModel? SelectedAssignedStage
    {
        get => _selectedAssignedStage;
        set
        {
            if (SetProperty(ref _selectedAssignedStage, value))
            {
                SelectedSubStage = value?.SubStages.FirstOrDefault();
                OnPropertyChanged(nameof(SelectedPresetTitle));
            }
        }
    }

    public BuildingTypeSubStageViewModel? SelectedSubStage
    {
        get => _selectedSubStage;
        set
        {
            if (SetProperty(ref _selectedSubStage, value))
            {
                OnPropertyChanged(nameof(MaterialsTitle));
            }
        }
    }

    public BuildingTypeStageEditorDto? SelectedAvailablePreset
    {
        get => _selectedAvailablePreset;
        set => SetProperty(ref _selectedAvailablePreset, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RefreshMasterList();
            }
        }
    }

    public bool ActiveOnly
    {
        get => _activeOnly;
        set
        {
            if (SetProperty(ref _activeOnly, value))
            {
                RefreshMasterList();
            }
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                IsDirty = true;
            }
        }
    }

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(CanDeactivate));
                OnPropertyChanged(nameof(CanReactivate));
                IsDirty = true;
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public bool CanDeactivate => _editingId.HasValue && IsActive;
    public bool CanReactivate => _editingId.HasValue && !IsActive;

    public string SelectedPresetTitle =>
        SelectedAssignedStage?.Name ?? ResourceHelper.GetString(
            "AdminHubView_SelectedPresetTitle",
            "Select a preset to preview its sub-stages");

    public string MaterialsTitle =>
        SelectedSubStage == null
            ? ResourceHelper.GetString(
                "AdminHubView_SelectedSubStageMaterialsTitle",
                "Select a sub-stage to edit materials")
            : string.Format(
                ResourceHelper.GetString(
                    "AdminHubView_MaterialsForSubStageFormat",
                    "Materials for \"{0}\""),
                SelectedSubStage.Name);

    public async Task LoadAsync(bool beginNew = true)
    {
        var selectedId = SelectedBuildingType?.Id ?? _editingId;
        var buildingTypes = await _dispatcher.Send(
            new ListBuildingTypesForAdminQuery());
        var presets = await _dispatcher.Send(new ListStagePresetsQuery());
        _workspace = await _dispatcher.Send(
            new GetBuildingTypeEditorQuery(selectedId));

        _allBuildingTypes.Clear();
        _allBuildingTypes.AddRange(buildingTypes);
        _stagePresetCatalog.Clear();
        _stagePresetCatalog.AddRange(presets);
        RefreshMasterList();

        if (selectedId.HasValue &&
            _allBuildingTypes.FirstOrDefault(item => item.Id == selectedId) is { } selected)
        {
            SelectedBuildingType = selected;
            await SelectAsync(selected);
        }
        else if (beginNew)
        {
            BeginNew();
        }
    }

    public async Task SelectAsync(BuildingTypeListItemDto? buildingType)
    {
        SelectedBuildingType = buildingType;
        var version = ++_selectionVersion;
        if (buildingType == null)
        {
            return;
        }

        var workspace = await _dispatcher.Send(
            new GetBuildingTypeEditorQuery(buildingType.Id));
        if (version != _selectionVersion ||
            SelectedBuildingType?.Id != buildingType.Id ||
            workspace?.BuildingType == null)
        {
            return;
        }

        _workspace = workspace;
        _editingId = buildingType.Id;
        Name = workspace.BuildingType.Name;
        SetActive(workspace.BuildingType.IsActive, markDirty: false);
        AssignedStages.Clear();
        foreach (var stage in workspace.BuildingType.AssignedStages
                     .OrderBy(item => item.OrderIndex))
        {
            AssignedStages.Add(CreateAssignedStage(stage));
        }
        SelectedAssignedStage = AssignedStages.FirstOrDefault();
        RefreshPresetPicker();
        IsDirty = false;
    }

    public void BeginNew()
    {
        _selectionVersion++;
        _editingId = null;
        SelectedBuildingType = null;
        Name = string.Empty;
        SetActive(true, markDirty: false);
        AssignedStages.Clear();
        SelectedAssignedStage = null;
        RefreshPresetPicker();
        IsDirty = false;
        OnPropertyChanged(nameof(CanDeactivate));
        OnPropertyChanged(nameof(CanReactivate));
    }

    public async Task RefreshReferencesPreservingDraftAsync()
    {
        var templates = await _dispatcher.Send(
            new GetBuildingTypeEditorQuery(null));
        var presets = await _dispatcher.Send(new ListStagePresetsQuery());
        if (templates == null)
        {
            return;
        }

        _workspace = templates;
        _stagePresetCatalog.Clear();
        _stagePresetCatalog.AddRange(presets);
        foreach (var assigned in AssignedStages)
        {
            var template = templates.StageTemplates.FirstOrDefault(
                item => item.StagePresetId == assigned.StagePresetId);
            if (template == null)
            {
                continue;
            }
            assigned.Name = template.Name;
            MergeTemplate(assigned, template);
        }
        RefreshPresetPicker();
    }

    public void AddPreset()
    {
        var template = SelectedAvailablePreset;
        if (template == null)
        {
            _interaction.Information(
                ResourceHelper.GetString(
                    "AdminHubView_SelectPresetToAddMessage",
                    "Select a stage preset to add."),
                ResourceHelper.GetString("Common_RequiredTitle", "Required"));
            return;
        }
        if (AssignedStages.Any(item =>
                item.StagePresetId == template.StagePresetId))
        {
            return;
        }

        var assigned = CreateAssignedStage(template);
        assigned.OrderIndex = AssignedStages.Count + 1;
        AssignedStages.Add(assigned);
        SelectedAssignedStage = assigned;
        RefreshPresetPicker();
        IsDirty = true;
    }

    public void MoveAssignedStage(
        BuildingTypeAssignedStageViewModel? stage,
        int direction)
    {
        if (stage == null)
        {
            return;
        }
        var index = AssignedStages.IndexOf(stage);
        var destination = index + direction;
        if (index < 0 || destination < 0 || destination >= AssignedStages.Count)
        {
            return;
        }
        AssignedStages.Move(index, destination);
        Renumber();
        IsDirty = true;
    }

    public void RemoveAssignedStage(BuildingTypeAssignedStageViewModel? stage)
    {
        if (stage == null)
        {
            return;
        }
        AssignedStages.Remove(stage);
        Renumber();
        SelectedAssignedStage = AssignedStages.FirstOrDefault();
        RefreshPresetPicker();
        IsDirty = true;
    }

    public async Task<bool> SaveAsync(
        bool keepSelection = true,
        bool showSuccess = true)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            _interaction.Warning(
                ResourceHelper.GetString(
                    "AdminHubView_BuildingTypeNameRequired",
                    "Name is required."),
                ResourceHelper.GetString("Common_ValidationTitle", "Validation"));
            return false;
        }

        var definition = new BuildingTypeDefinition(
            _editingId,
            Name.Trim(),
            IsActive,
            AssignedStages.Select((stage, index) =>
                new BuildingTypeStageDefinition(
                    stage.StagePresetId,
                    index + 1,
                    stage.SubStages.Select(subStage =>
                        new BuildingTypeSubStageDefinition(
                            subStage.SubStagePresetId,
                            subStage.LaborCost,
                            subStage.IncludeInReport,
                            subStage.Materials.Select(material =>
                                new BuildingTypeMaterialDefinition(
                                    material.MaterialId,
                                    material.Qty))
                                .ToList()))
                        .ToList()))
                .ToList());
        var result = await _dispatcher.Send(
            new SaveBuildingTypeCommand(definition));
        if (result.IsFailure || result.Value <= 0)
        {
            _interaction.Warning(
                result.Error?.Message ?? ResourceHelper.GetString(
                    "AdminHubView_SaveBuildingTypeFailedFormat",
                    "Saving failed."),
                ResourceHelper.GetString("Common_ValidationTitle", "Validation"));
            return false;
        }

        _editingId = result.Value;
        await LoadAsync(beginNew: false);
        if (keepSelection &&
            _allBuildingTypes.FirstOrDefault(item => item.Id == result.Value) is { } selected)
        {
            SelectedBuildingType = selected;
            await SelectAsync(selected);
        }
        if (showSuccess)
        {
            _interaction.Information(
                ResourceHelper.GetString(
                    "AdminHubView_BuildingTypeSavedMessage",
                    "Building type saved."),
                ResourceHelper.GetString("Common_SuccessTitle", "Success"));
        }
        IsDirty = false;
        return true;
    }

    public async Task DeactivateAsync()
    {
        if (!_editingId.HasValue ||
            !_interaction.Confirm(
                string.Format(
                    ResourceHelper.GetString(
                        "AdminHubView_DeleteBuildingTypeConfirmFormat",
                        "Delete building type '{0}'? This will deactivate it."),
                    DisplayName()),
                ResourceHelper.GetString("Common_ConfirmTitle", "Confirm")))
        {
            return;
        }
        var previous = IsActive;
        IsActive = false;
        if (await SaveAsync(keepSelection: false))
        {
            BeginNew();
        }
        else
        {
            SetActive(previous, markDirty: false);
        }
    }

    public async Task ReactivateAsync()
    {
        if (!_editingId.HasValue ||
            !_interaction.Confirm(
                string.Format(
                    ResourceHelper.GetString(
                        "AdminHubView_ReactivateBuildingTypeConfirmFormat",
                        "Reactivate building type '{0}'?"),
                    DisplayName()),
                ResourceHelper.GetString("Common_ConfirmTitle", "Confirm")))
        {
            return;
        }
        var previous = IsActive;
        IsActive = true;
        if (!await SaveAsync())
        {
            SetActive(previous, markDirty: false);
        }
    }

    public async Task CancelAsync()
    {
        if (SelectedBuildingType == null)
        {
            BeginNew();
        }
        else
        {
            await SelectAsync(SelectedBuildingType);
        }
    }

    private BuildingTypeAssignedStageViewModel CreateAssignedStage(
        BuildingTypeStageEditorDto source)
    {
        var assigned = new BuildingTypeAssignedStageViewModel
        {
            StagePresetId = source.StagePresetId,
            Name = source.Name,
            OrderIndex = source.OrderIndex,
            SubStageCount = source.SubStages.Count
        };
        foreach (var sourceSubStage in source.SubStages.OrderBy(item => item.OrderIndex))
        {
            var subStage = new BuildingTypeSubStageViewModel
            {
                StagePresetId = source.StagePresetId,
                SubStagePresetId = sourceSubStage.Id,
                Name = sourceSubStage.Name,
                OrderIndex = sourceSubStage.OrderIndex,
                LaborCost = sourceSubStage.LaborCost,
                IncludeInReport = sourceSubStage.IncludeInReport
            };
            subStage.PropertyChanged += ChildChanged;
            foreach (var sourceMaterial in sourceSubStage.Materials)
            {
                var material = new BuildingTypeMaterialViewModel
                {
                    MaterialId = sourceMaterial.MaterialId,
                    MaterialName = sourceMaterial.MaterialName,
                    Unit = sourceMaterial.Unit,
                    Qty = sourceMaterial.Quantity
                };
                material.PropertyChanged += ChildChanged;
                subStage.Materials.Add(material);
            }
            assigned.SubStages.Add(subStage);
        }
        return assigned;
    }

    private void MergeTemplate(
        BuildingTypeAssignedStageViewModel assigned,
        BuildingTypeStageEditorDto template)
    {
        var existingSubStages = assigned.SubStages.ToDictionary(
            item => item.SubStagePresetId);
        assigned.SubStages.Clear();
        foreach (var source in template.SubStages.OrderBy(item => item.OrderIndex))
        {
            var subStage = existingSubStages.GetValueOrDefault(source.Id) ??
                           new BuildingTypeSubStageViewModel
                           {
                               StagePresetId = template.StagePresetId,
                               SubStagePresetId = source.Id,
                               LaborCost = source.LaborCost,
                               IncludeInReport = source.IncludeInReport
                           };
            subStage.Name = source.Name;
            subStage.OrderIndex = source.OrderIndex;
            var quantities = subStage.Materials.ToDictionary(
                item => item.MaterialId,
                item => item.Qty);
            subStage.Materials.Clear();
            foreach (var sourceMaterial in source.Materials)
            {
                var material = new BuildingTypeMaterialViewModel
                {
                    MaterialId = sourceMaterial.MaterialId,
                    MaterialName = sourceMaterial.MaterialName,
                    Unit = sourceMaterial.Unit,
                    Qty = quantities.GetValueOrDefault(sourceMaterial.MaterialId)
                };
                material.PropertyChanged += ChildChanged;
                subStage.Materials.Add(material);
            }
            subStage.PropertyChanged -= ChildChanged;
            subStage.PropertyChanged += ChildChanged;
            assigned.SubStages.Add(subStage);
        }
        assigned.SubStageCount = assigned.SubStages.Count;
    }

    private void ChildChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is
            nameof(BuildingTypeSubStageViewModel.LaborCost) or
            nameof(BuildingTypeSubStageViewModel.IncludeInReport) or
            nameof(BuildingTypeMaterialViewModel.Qty))
        {
            IsDirty = true;
        }
    }

    private void RefreshMasterList()
    {
        var selectedId = SelectedBuildingType?.Id;
        IEnumerable<BuildingTypeListItemDto> query = _allBuildingTypes;
        if (ActiveOnly)
        {
            query = query.Where(item => item.IsActive);
        }
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(item =>
                item.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        BuildingTypes.Clear();
        foreach (var item in query.OrderBy(item => item.Name))
        {
            BuildingTypes.Add(item);
        }
        SelectedBuildingType =
            BuildingTypes.FirstOrDefault(item => item.Id == selectedId);
    }

    private void RefreshPresetPicker()
    {
        var assignedIds = AssignedStages.Select(item => item.StagePresetId).ToHashSet();
        var activeIds = _stagePresetCatalog
            .Where(item => item.IsActive)
            .Select(item => item.Id)
            .ToHashSet();
        AvailablePresets.Clear();
        var templates = _workspace?.StageTemplates
            .Where(item =>
                activeIds.Contains(item.StagePresetId) &&
                !assignedIds.Contains(item.StagePresetId))
            .OrderBy(item => item.Name)
            ?? Enumerable.Empty<BuildingTypeStageEditorDto>();
        foreach (var template in templates)
        {
            AvailablePresets.Add(template);
        }
        SelectedAvailablePreset = AvailablePresets.FirstOrDefault();
    }

    private void Renumber()
    {
        for (var index = 0; index < AssignedStages.Count; index++)
        {
            AssignedStages[index].OrderIndex = index + 1;
        }
    }

    private void SetActive(bool value, bool markDirty)
    {
        _isActive = value;
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CanDeactivate));
        OnPropertyChanged(nameof(CanReactivate));
        if (markDirty)
        {
            IsDirty = true;
        }
    }

    private string DisplayName() => string.IsNullOrWhiteSpace(Name)
        ? ResourceHelper.GetString(
            "AdminHubView_UnnamedBuildingTypeLabel",
            "this building type")
        : Name.Trim();
}

public sealed class BuildingTypeAssignedStageViewModel : ObservableObject
{
    private string _name = string.Empty;
    private int _orderIndex;
    private int _subStageCount;

    public int StagePresetId { get; init; }
    public ObservableCollection<BuildingTypeSubStageViewModel> SubStages { get; } = [];

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public int OrderIndex
    {
        get => _orderIndex;
        set => SetProperty(ref _orderIndex, value);
    }

    public int SubStageCount
    {
        get => _subStageCount;
        set => SetProperty(ref _subStageCount, value);
    }
}

public sealed class BuildingTypeSubStageViewModel : ObservableObject
{
    private string _name = string.Empty;
    private int _orderIndex;
    private decimal? _laborCost;
    private bool _includeInReport;

    public int StagePresetId { get; init; }
    public int SubStagePresetId { get; init; }
    public ObservableCollection<BuildingTypeMaterialViewModel> Materials { get; } = [];
    public bool CanModifyInclude => true;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public int OrderIndex
    {
        get => _orderIndex;
        set => SetProperty(ref _orderIndex, value);
    }

    public decimal? LaborCost
    {
        get => _laborCost;
        set => SetProperty(ref _laborCost, value);
    }

    public bool IncludeInReport
    {
        get => _includeInReport;
        set => SetProperty(ref _includeInReport, value);
    }
}

public sealed class BuildingTypeMaterialViewModel : ObservableObject
{
    private decimal? _qty;

    public int MaterialId { get; init; }
    public string MaterialName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;

    public decimal? Qty
    {
        get => _qty;
        set => SetProperty(ref _qty, value);
    }
}
