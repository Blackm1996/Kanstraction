using Kanstraction.OnSite.Application.StagePresets.Queries.ListStagePresets;
using Kanstraction.Services;
using System.Collections.ObjectModel;

namespace Kanstraction.ViewModels.OnSite.Administration;

public sealed class StagePresetCatalogViewModel : ObservableObject
{
    private readonly IApplicationDispatcher _dispatcher;
    private readonly List<StagePresetListItemDto> _allPresets = [];
    private StagePresetListItemDto? _selectedPreset;
    private string _searchText = string.Empty;
    private bool _activeOnly = true;

    public StagePresetCatalogViewModel(IApplicationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public ObservableCollection<StagePresetListItemDto> Presets { get; } = [];
    public IReadOnlyList<StagePresetListItemDto> AllPresets => _allPresets;

    public StagePresetListItemDto? SelectedPreset
    {
        get => _selectedPreset;
        set => SetProperty(ref _selectedPreset, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                Refresh();
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
                Refresh();
            }
        }
    }

    public async Task LoadAsync(int? preferredPresetId = null)
    {
        var selectedId = preferredPresetId ?? SelectedPreset?.Id;
        var presets = await _dispatcher.Send(new ListStagePresetsQuery());
        _allPresets.Clear();
        _allPresets.AddRange(presets);
        Refresh();
        SelectedPreset = Presets.FirstOrDefault(item => item.Id == selectedId);
    }

    public void BeginNew() => SelectedPreset = null;

    private void Refresh()
    {
        var selectedId = SelectedPreset?.Id;
        IEnumerable<StagePresetListItemDto> query = _allPresets;
        if (ActiveOnly)
        {
            query = query.Where(item => item.IsActive);
        }
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(item =>
                item.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        Presets.Clear();
        foreach (var preset in query.OrderBy(item => item.Name))
        {
            Presets.Add(preset);
        }
        SelectedPreset = Presets.FirstOrDefault(item => item.Id == selectedId);
    }
}
