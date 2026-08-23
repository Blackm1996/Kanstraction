using Kanstraction.OnSite.Application.Materials.Queries.ListAvailableMaterials;
using Kanstraction.OnSite.Application.StagePresets.Commands.SaveStagePreset;
using Kanstraction.OnSite.Application.StagePresets.Queries.GetStagePresetEditor;
using Kanstraction.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Kanstraction.ViewModels.OnSite;

public sealed class StagePresetDesignerViewModel : ObservableObject
{
    private IApplicationDispatcher? _dispatcher;
    private IUserInteractionService? _interaction;
    private int? _currentPresetId;
    private StagePresetEditorDto? _loadedPreset;
    private StagePresetSubStageViewModel? _selectedSubStage;
    private AvailableMaterialDto? _selectedAvailableMaterial;
    private string _name = string.Empty;
    private bool _isActive = true;
    private bool _isDirty;
    private bool _isEmpty = true;
    private int _loadVersion;

    public StagePresetDesignerViewModel()
    {
    }

    public StagePresetDesignerViewModel(
        IApplicationDispatcher dispatcher,
        IUserInteractionService interaction)
    {
        Configure(dispatcher, interaction);
    }

    public event EventHandler<int>? Saved;

    public ObservableCollection<StagePresetSubStageViewModel> SubStages { get; } = [];
    public ObservableCollection<AvailableMaterialDto> AvailableMaterials { get; } = [];
    public List<AvailableMaterialDto> MaterialReferences { get; private set; } = [];

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

    public bool IsEmpty
    {
        get => _isEmpty;
        private set
        {
            if (SetProperty(ref _isEmpty, value))
            {
                OnPropertyChanged(nameof(IsEditorVisible));
            }
        }
    }

    public bool IsEditorVisible => !IsEmpty;
    public bool CanDeactivate => _currentPresetId.HasValue && IsActive;
    public bool CanReactivate => _currentPresetId.HasValue && !IsActive;
    public bool ShouldResetAfterSave { get; private set; }

    public string Summary => string.Format(
        ResourceHelper.GetString(
            "StagePresetDesignerView_TotalSubStages",
            "Total sub-stages: {0}"),
        SubStages.Count);

    public StagePresetSubStageViewModel? SelectedSubStage
    {
        get => _selectedSubStage;
        set
        {
            if (SetProperty(ref _selectedSubStage, value))
            {
                OnPropertyChanged(nameof(HasSelectedSubStage));
                RefreshAvailableMaterials();
            }
        }
    }

    public bool HasSelectedSubStage => SelectedSubStage != null;

    public AvailableMaterialDto? SelectedAvailableMaterial
    {
        get => _selectedAvailableMaterial;
        set => SetProperty(ref _selectedAvailableMaterial, value);
    }

    public void Configure(
        IApplicationDispatcher dispatcher,
        IUserInteractionService interaction)
    {
        _dispatcher = dispatcher;
        _interaction = interaction;
    }

    public void EnterEmptyState()
    {
        _loadVersion++;
        _currentPresetId = null;
        _loadedPreset = null;
        Name = string.Empty;
        SubStages.Clear();
        SelectedSubStage = null;
        SetActive(false, markDirty: false);
        IsDirty = false;
        IsEmpty = true;
        UpdateSummary();
    }

    public async Task LoadPresetAsync(int? presetId)
    {
        var version = ++_loadVersion;
        var workspace = await Dispatcher.Send(
            new GetStagePresetEditorQuery(presetId));
        if (version != _loadVersion || workspace == null)
        {
            return;
        }

        SetAvailableMaterials(workspace.ActiveMaterials);
        IsEmpty = false;
        _currentPresetId = presetId;
        SubStages.Clear();

        if (presetId == null)
        {
            _loadedPreset = null;
            Name = string.Empty;
            SetActive(true, markDirty: false);
            SelectedSubStage = null;
            IsDirty = false;
            UpdateSummary();
            return;
        }

        if (workspace.Preset == null)
        {
            Interaction.Warning(
                ResourceHelper.GetString(
                    "StagePresetDesignerView_PresetNotFound",
                    "Preset not found."),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"));
            return;
        }

        _loadedPreset = workspace.Preset;
        Name = workspace.Preset.Name;
        SetActive(workspace.Preset.IsActive, markDirty: false);
        foreach (var item in workspace.Preset.SubStages.OrderBy(item => item.OrderIndex))
        {
            var subStage = new StagePresetSubStageViewModel
            {
                Id = item.Id,
                Name = item.Name,
                OrderIndex = item.OrderIndex,
                Materials = new ObservableCollection<StagePresetMaterialUsageViewModel>(
                    item.Materials.Select(material => new StagePresetMaterialUsageViewModel
                    {
                        Id = material.UsageId,
                        MaterialId = material.MaterialId,
                        MaterialName = material.MaterialName,
                        CategoryName = material.CategoryName,
                        Unit = material.Unit
                    }))
            };
            Subscribe(subStage);
            SubStages.Add(subStage);
        }
        SelectedSubStage = SubStages.FirstOrDefault();
        IsDirty = false;
        UpdateSummary();
    }

    public async Task RefreshAvailableMaterialsAsync()
    {
        var workspace = await Dispatcher.Send(
            new GetStagePresetEditorQuery(null));
        if (workspace != null)
        {
            SetAvailableMaterials(workspace.ActiveMaterials);
        }
    }

    public void SetAvailableMaterials(
        IEnumerable<AvailableMaterialDto> activeMaterials,
        IEnumerable<AvailableMaterialDto>? references = null)
    {
        var active = activeMaterials.ToList();
        MaterialReferences = (references ?? active).ToList();
        var metadata = MaterialReferences.ToDictionary(item => item.Id);
        foreach (var usage in SubStages.SelectMany(item => item.Materials))
        {
            if (metadata.TryGetValue(usage.MaterialId, out var material))
            {
                usage.MaterialName = material.Name;
                usage.CategoryName = material.CategoryName;
                usage.Unit = material.Unit;
            }
        }
        _activeMaterials = active;
        RefreshAvailableMaterials();
    }

    private List<AvailableMaterialDto> _activeMaterials = [];

    public void AddSubStage()
    {
        var subStage = new StagePresetSubStageViewModel
        {
            OrderIndex = SubStages.Count + 1,
            Materials = []
        };
        Subscribe(subStage);
        SubStages.Add(subStage);
        SelectedSubStage = subStage;
        IsDirty = true;
        UpdateSummary();
    }

    public void DeleteSubStage(StagePresetSubStageViewModel? subStage)
    {
        if (subStage == null ||
            !Interaction.Confirm(
                string.Format(
                    ResourceHelper.GetString(
                        "StagePresetDesignerView_DeleteSubStageConfirmFormat",
                        "Delete sub-stage '{0}'?"),
                    subStage.Name),
                ResourceHelper.GetString("Common_ConfirmTitle", "Confirm"),
                isWarning: true))
        {
            return;
        }
        var index = SubStages.IndexOf(subStage);
        SubStages.Remove(subStage);
        Reindex();
        SelectedSubStage = SubStages.ElementAtOrDefault(
            Math.Min(index, SubStages.Count - 1));
        IsDirty = true;
        UpdateSummary();
    }

    public void MoveSubStage(StagePresetSubStageViewModel? subStage, int direction)
    {
        if (subStage == null)
        {
            return;
        }
        var index = SubStages.IndexOf(subStage);
        var destination = index + direction;
        if (index < 0 || destination < 0 || destination >= SubStages.Count)
        {
            return;
        }
        SubStages.Move(index, destination);
        Reindex();
        IsDirty = true;
    }

    public void AddMaterial()
    {
        if (SelectedSubStage == null)
        {
            Interaction.Information(
                ResourceHelper.GetString(
                    "StagePresetDesignerView_SelectSubStageFirst",
                    "Select a sub-stage first."),
                ResourceHelper.GetString("Common_RequiredTitle", "Required"));
            return;
        }
        if (SelectedAvailableMaterial == null)
        {
            Interaction.Warning(
                ResourceHelper.GetString(
                    "StagePresetDesignerView_SelectMaterial",
                    "Choose a material."),
                ResourceHelper.GetString("Common_RequiredTitle", "Required"));
            return;
        }
        if (SelectedSubStage.Materials.Any(item =>
                item.MaterialId == SelectedAvailableMaterial.Id))
        {
            return;
        }

        SelectedSubStage.Materials.Add(new StagePresetMaterialUsageViewModel
        {
            MaterialId = SelectedAvailableMaterial.Id,
            MaterialName = SelectedAvailableMaterial.Name,
            CategoryName = SelectedAvailableMaterial.CategoryName,
            Unit = SelectedAvailableMaterial.Unit
        });
        SelectedAvailableMaterial = null;
        IsDirty = true;
        RefreshAvailableMaterials();
    }

    public void RemoveMaterial(StagePresetMaterialUsageViewModel? material)
    {
        if (material == null || SelectedSubStage == null)
        {
            return;
        }
        SelectedSubStage.Materials.Remove(material);
        IsDirty = true;
        RefreshAvailableMaterials();
    }

    public async Task<bool> SaveAsync(
        bool resetAfterSave = false,
        bool showSuccess = true)
    {
        var definition = new StagePresetDefinition(
            _currentPresetId,
            Name.Trim(),
            IsActive,
            SubStages.Select(subStage => new StagePresetSubStageDefinition(
                subStage.Id,
                subStage.Name.Trim(),
                subStage.OrderIndex,
                subStage.Materials.Select(material =>
                    new StagePresetMaterialDefinition(
                        material.Id,
                        material.MaterialId))
                    .ToList()))
                .ToList());
        var result = await Dispatcher.Send(new SaveStagePresetCommand(definition));
        if (result.IsFailure || result.Value <= 0)
        {
            Interaction.Warning(
                result.Error?.Message ?? ResourceHelper.GetString(
                    "StagePresetDesignerView_SaveFailedFormat",
                    "Saving failed."),
                ResourceHelper.GetString("Common_ValidationTitle", "Validation"));
            return false;
        }

        _currentPresetId = result.Value;
        var refreshed = await Dispatcher.Send(
            new GetStagePresetEditorQuery(result.Value));
        _loadedPreset = refreshed?.Preset;
        IsDirty = false;
        ShouldResetAfterSave = resetAfterSave;
        if (showSuccess)
        {
            Interaction.Information(
                ResourceHelper.GetString(
                    "StagePresetDesignerView_SavedMessage",
                    "Stage preset saved."),
                ResourceHelper.GetString(
                    "StagePresetDesignerView_DialogTitle",
                    "Stage preset"));
        }
        Saved?.Invoke(this, result.Value);
        ShouldResetAfterSave = false;
        return true;
    }

    public async Task DeactivateAsync()
    {
        if (!_currentPresetId.HasValue ||
            !Interaction.Confirm(
                string.Format(
                    ResourceHelper.GetString(
                        "StagePresetDesignerView_DeletePresetConfirmFormat",
                        "Delete stage preset '{0}'? This will deactivate it."),
                    DisplayName()),
                ResourceHelper.GetString("Common_ConfirmTitle", "Confirm")))
        {
            return;
        }
        var previous = IsActive;
        IsActive = false;
        if (!await SaveAsync(resetAfterSave: true))
        {
            SetActive(previous, markDirty: false);
        }
    }

    public async Task ReactivateAsync()
    {
        if (!_currentPresetId.HasValue ||
            !Interaction.Confirm(
                string.Format(
                    ResourceHelper.GetString(
                        "StagePresetDesignerView_ReactivatePresetConfirmFormat",
                        "Reactivate stage preset '{0}'?"),
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

    public Task CancelAsync() => LoadPresetAsync(_currentPresetId);

    private void RefreshAvailableMaterials()
    {
        var selectedId = SelectedAvailableMaterial?.Id;
        var used = SelectedSubStage?.Materials
            .Select(item => item.MaterialId)
            .ToHashSet() ?? [];
        AvailableMaterials.Clear();
        foreach (var material in _activeMaterials.Where(item => !used.Contains(item.Id)))
        {
            AvailableMaterials.Add(material);
        }
        SelectedAvailableMaterial =
            AvailableMaterials.FirstOrDefault(item => item.Id == selectedId);
    }

    private void Subscribe(StagePresetSubStageViewModel subStage)
    {
        subStage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(StagePresetSubStageViewModel.Name))
            {
                IsDirty = true;
            }
        };
    }

    private void Reindex()
    {
        for (var index = 0; index < SubStages.Count; index++)
        {
            SubStages[index].OrderIndex = index + 1;
        }
    }

    private void UpdateSummary() => OnPropertyChanged(nameof(Summary));

    private void SetActive(bool active, bool markDirty)
    {
        _isActive = active;
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
            "StagePresetDesignerView_UnnamedPresetLabel",
            "this stage preset")
        : Name.Trim();

    private IApplicationDispatcher Dispatcher =>
        _dispatcher ?? throw new InvalidOperationException(
            "The stage preset designer has not been configured.");

    private IUserInteractionService Interaction =>
        _interaction ?? throw new InvalidOperationException(
            "The stage preset designer has not been configured.");
}

public sealed class StagePresetSubStageViewModel : INotifyPropertyChanged
{
    private int? _id;
    private string _name = string.Empty;
    private int _orderIndex;
    private ObservableCollection<StagePresetMaterialUsageViewModel> _materials = [];

    public int? Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

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

    public ObservableCollection<StagePresetMaterialUsageViewModel> Materials
    {
        get => _materials;
        set => SetProperty(ref _materials, value ?? []);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class StagePresetMaterialUsageViewModel : ObservableObject
{
    private string _materialName = string.Empty;
    private string _categoryName = string.Empty;
    private string _unit = string.Empty;

    public int? Id { get; set; }
    public int MaterialId { get; set; }

    public string MaterialName
    {
        get => _materialName;
        set => SetProperty(ref _materialName, value ?? string.Empty);
    }

    public string CategoryName
    {
        get => _categoryName;
        set => SetProperty(ref _categoryName, value ?? string.Empty);
    }

    public string Unit
    {
        get => _unit;
        set => SetProperty(ref _unit, value ?? string.Empty);
    }
}
