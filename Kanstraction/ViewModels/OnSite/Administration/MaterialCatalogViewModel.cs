using Kanstraction.OnSite.Application.Materials.Commands.CreateMaterialCategory;
using Kanstraction.OnSite.Application.Materials.Commands.SaveMaterial;
using Kanstraction.OnSite.Application.Materials.Queries.GetMaterialDetails;
using Kanstraction.OnSite.Application.Materials.Queries.ListMaterialCategoriesForAdmin;
using Kanstraction.OnSite.Application.Materials.Queries.ListMaterialsForAdmin;
using Kanstraction.Services;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Kanstraction.ViewModels.OnSite.Administration;

public sealed record MaterialCategoryFilter(int? Id, string Name);
public sealed record MaterialPriceHistoryRow(string StartDate, string EndDate, decimal PricePerUnit);
public sealed record SimilarMaterialSuggestion(string Name, string Unit);

public sealed class MaterialCatalogViewModel : ObservableObject
{
    private readonly IApplicationDispatcher _dispatcher;
    private readonly IUserInteractionService _interaction;
    private readonly List<MaterialListItemDto> _allMaterials = [];
    private MaterialDetailsDto? _loadedMaterial;
    private MaterialListItemDto? _selectedMaterial;
    private string _searchText = string.Empty;
    private bool _activeOnly = true;
    private int? _filterCategoryId;
    private int? _editingMaterialId;
    private string _name = string.Empty;
    private string _unit = string.Empty;
    private string _priceText = string.Empty;
    private int? _selectedCategoryId;
    private bool _isActive = true;
    private bool _isDirty;
    private bool _isBusy;
    private int _selectionVersion;

    public MaterialCatalogViewModel(
        IApplicationDispatcher dispatcher,
        IUserInteractionService interaction)
    {
        _dispatcher = dispatcher;
        _interaction = interaction;
    }

    public event EventHandler? MaterialsChanged;

    public ObservableCollection<MaterialListItemDto> Materials { get; } = [];
    public ObservableCollection<MaterialCategoryListItemDto> Categories { get; } = [];
    public ObservableCollection<MaterialCategoryFilter> CategoryFilters { get; } = [];
    public ObservableCollection<MaterialPriceHistoryRow> PriceHistory { get; } = [];
    public ObservableCollection<SimilarMaterialSuggestion> SimilarMaterials { get; } = [];

    public MaterialListItemDto? SelectedMaterial
    {
        get => _selectedMaterial;
        set => SetProperty(ref _selectedMaterial, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RefreshList();
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
                RefreshList();
            }
        }
    }

    public int? FilterCategoryId
    {
        get => _filterCategoryId;
        set
        {
            if (SetProperty(ref _filterCategoryId, value))
            {
                RefreshList();
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
                UpdateDirtyState();
                UpdateSimilarMaterials();
            }
        }
    }

    public string Unit
    {
        get => _unit;
        set
        {
            if (SetProperty(ref _unit, value ?? string.Empty))
            {
                UpdateDirtyState();
            }
        }
    }

    public string PriceText
    {
        get => _priceText;
        set
        {
            if (SetProperty(ref _priceText, value ?? string.Empty))
            {
                UpdateDirtyState();
            }
        }
    }

    public int? SelectedCategoryId
    {
        get => _selectedCategoryId;
        set
        {
            if (SetProperty(ref _selectedCategoryId, value))
            {
                UpdateDirtyState();
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
                UpdateDirtyState();
            }
        }
    }

    public bool CanDeactivate => _editingMaterialId.HasValue && IsActive;
    public bool CanReactivate => _editingMaterialId.HasValue && !IsActive;
    public bool HasSimilarMaterials => SimilarMaterials.Count > 0;

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public IReadOnlyList<MaterialListItemDto> AllMaterials => _allMaterials;

    public async Task LoadAsync(bool beginNew = true)
    {
        IsBusy = true;
        try
        {
            var categories = await _dispatcher.Send(
                new ListMaterialCategoriesForAdminQuery());
            var materials = await _dispatcher.Send(
                new ListMaterialsForAdminQuery());

            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }
            _allMaterials.Clear();
            _allMaterials.AddRange(materials);
            RefreshCategoryFilters();
            RefreshList();

            if (beginNew)
            {
                BeginNew();
            }
            else if (_editingMaterialId.HasValue)
            {
                await SelectByIdAsync(_editingMaterialId.Value);
            }
            MaterialsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SelectAsync(MaterialListItemDto? material)
    {
        SelectedMaterial = material;
        var version = ++_selectionVersion;
        if (material == null)
        {
            BeginNew(clearSelection: false);
            return;
        }

        var details = await _dispatcher.Send(
            new GetMaterialDetailsQuery(material.Id));
        if (version != _selectionVersion ||
            SelectedMaterial?.Id != material.Id ||
            details == null)
        {
            return;
        }

        _editingMaterialId = details.Id;
        _loadedMaterial = details;
        Apply(details);
    }

    public void BeginNew(bool clearSelection = true)
    {
        _selectionVersion++;
        _editingMaterialId = null;
        _loadedMaterial = null;
        if (clearSelection)
        {
            SelectedMaterial = null;
        }
        Name = string.Empty;
        Unit = string.Empty;
        PriceText = string.Empty;
        SelectedCategoryId = null;
        IsActive = true;
        PriceHistory.Clear();
        SimilarMaterials.Clear();
        OnPropertyChanged(nameof(HasSimilarMaterials));
        OnPropertyChanged(nameof(CanDeactivate));
        OnPropertyChanged(nameof(CanReactivate));
        IsDirty = false;
    }

    public void Cancel()
    {
        if (_loadedMaterial == null)
        {
            BeginNew();
        }
        else
        {
            Apply(_loadedMaterial);
        }
    }

    public async Task AddCategoryAsync()
    {
        var value = _interaction.PromptText(
            ResourceHelper.GetString(
                "AdminHubView_NewCategoryDialogTitle",
                "New category"));
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var result = await _dispatcher.Send(
            new CreateMaterialCategoryCommand(value.Trim()));
        if (result.IsFailure || result.Value <= 0)
        {
            _interaction.Warning(
                result.Error?.Message ?? ResourceHelper.GetString(
                    "AdminHubView_CategoryExistsMessage",
                    "A category with this name already exists."),
                ResourceHelper.GetString("Common_InvalidTitle", "Invalid"));
            return;
        }

        await LoadAsync(beginNew: false);
        SelectedCategoryId = result.Value;
    }

    public async Task<bool> SaveAsync(
        bool keepSelection = true,
        bool showSuccess = true)
    {
        if (!TryValidate(out var price, out var error))
        {
            _interaction.Warning(
                error,
                ResourceHelper.GetString("Common_ValidationTitle", "Validation"));
            return false;
        }

        var result = await _dispatcher.Send(new SaveMaterialCommand(
            _editingMaterialId,
            Name.Trim(),
            Unit.Trim(),
            price,
            DateTime.Today,
            IsActive,
            SelectedCategoryId!.Value));
        if (result.IsFailure || result.Value <= 0)
        {
            _interaction.Warning(
                result.Error?.Message ?? ResourceHelper.GetString(
                    "Common_UnknownError",
                    "The material could not be saved."),
                ResourceHelper.GetString("Common_ValidationTitle", "Validation"));
            return false;
        }

        _editingMaterialId = result.Value;
        await LoadAsync(beginNew: false);
        if (keepSelection)
        {
            await SelectByIdAsync(result.Value);
        }
        if (showSuccess)
        {
            _interaction.Information(
                ResourceHelper.GetString(
                    "AdminHubView_MaterialSavedMessage",
                    "Saved."),
                ResourceHelper.GetString(
                    "AdminHubView_MaterialDialogTitle",
                    "Material"));
        }
        IsDirty = false;
        MaterialsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task DeactivateAsync()
    {
        if (!_editingMaterialId.HasValue)
        {
            return;
        }
        var displayName = string.IsNullOrWhiteSpace(Name)
            ? ResourceHelper.GetString(
                "AdminHubView_UnnamedMaterialLabel",
                "this material")
            : Name.Trim();
        if (!_interaction.Confirm(
                string.Format(
                    ResourceHelper.GetString(
                        "AdminHubView_DeleteMaterialConfirmFormat",
                        "Delete material '{0}'? This will deactivate it."),
                    displayName),
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
            IsActive = previous;
        }
    }

    public async Task ReactivateAsync()
    {
        if (!_editingMaterialId.HasValue)
        {
            return;
        }
        var displayName = string.IsNullOrWhiteSpace(Name)
            ? ResourceHelper.GetString(
                "AdminHubView_UnnamedMaterialLabel",
                "this material")
            : Name.Trim();
        if (!_interaction.Confirm(
                string.Format(
                    ResourceHelper.GetString(
                        "AdminHubView_ReactivateMaterialConfirmFormat",
                        "Reactivate material '{0}'?"),
                    displayName),
                ResourceHelper.GetString("Common_ConfirmTitle", "Confirm")))
        {
            return;
        }

        var previous = IsActive;
        IsActive = true;
        if (!await SaveAsync())
        {
            IsActive = previous;
        }
    }

    private async Task SelectByIdAsync(int materialId)
    {
        RefreshList();
        SelectedMaterial = Materials.FirstOrDefault(item => item.Id == materialId);
        if (SelectedMaterial != null)
        {
            await SelectAsync(SelectedMaterial);
        }
    }

    private void Apply(MaterialDetailsDto material)
    {
        Name = material.Name;
        Unit = material.Unit;
        PriceText = material.PricePerUnit.ToString(CultureInfo.InvariantCulture);
        SelectedCategoryId = material.MaterialCategoryId;
        IsActive = material.IsActive;

        PriceHistory.Clear();
        var format = ResourceHelper.GetString("Common_DateFormat", "dd/MM/yyyy");
        foreach (var history in material.PriceHistory)
        {
            PriceHistory.Add(new MaterialPriceHistoryRow(
                history.StartDate.ToString(format),
                history.EndDate?.ToString(format) ?? string.Empty,
                history.PricePerUnit));
        }
        UpdateSimilarMaterials();
        IsDirty = false;
    }

    private void RefreshList()
    {
        IEnumerable<MaterialListItemDto> query = _allMaterials;
        if (ActiveOnly)
        {
            query = query.Where(item => item.IsActive);
        }
        if (FilterCategoryId.HasValue)
        {
            query = query.Where(item =>
                item.MaterialCategoryId == FilterCategoryId.Value);
        }
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(item =>
                item.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) ||
                item.Unit.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var selectedId = SelectedMaterial?.Id;
        Materials.Clear();
        foreach (var material in query.OrderBy(item => item.Name))
        {
            Materials.Add(material);
        }
        SelectedMaterial = Materials.FirstOrDefault(item => item.Id == selectedId);
    }

    private void RefreshCategoryFilters()
    {
        var previous = FilterCategoryId;
        CategoryFilters.Clear();
        CategoryFilters.Add(new MaterialCategoryFilter(
            null,
            ResourceHelper.GetString(
                "AdminHubView_CategoryFilterAll",
                "All categories")));
        foreach (var category in Categories)
        {
            CategoryFilters.Add(new MaterialCategoryFilter(
                category.Id,
                category.Name));
        }
        FilterCategoryId = CategoryFilters.Any(item => item.Id == previous)
            ? previous
            : null;
    }

    private void UpdateDirtyState()
    {
        if (_loadedMaterial == null)
        {
            IsDirty =
                !string.IsNullOrWhiteSpace(Name) ||
                !string.IsNullOrWhiteSpace(Unit) ||
                !string.IsNullOrWhiteSpace(PriceText) ||
                SelectedCategoryId.HasValue ||
                !IsActive;
            return;
        }

        IsDirty =
            !string.Equals(Name.Trim(), _loadedMaterial.Name, StringComparison.Ordinal) ||
            !string.Equals(Unit.Trim(), _loadedMaterial.Unit, StringComparison.Ordinal) ||
            !decimal.TryParse(
                PriceText.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var price) ||
            price != _loadedMaterial.PricePerUnit ||
            SelectedCategoryId != _loadedMaterial.MaterialCategoryId ||
            IsActive != _loadedMaterial.IsActive;
    }

    private bool TryValidate(out decimal price, out string error)
    {
        price = 0;
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = ResourceHelper.GetString(
                "AdminHubView_MaterialNameRequired",
                "Name is required.");
            return false;
        }
        if (!SelectedCategoryId.HasValue)
        {
            error = ResourceHelper.GetString(
                "AdminHubView_CategoryRequired",
                "Category is required.");
            return false;
        }
        if (!decimal.TryParse(
                PriceText.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out price) ||
            price < 0)
        {
            error = ResourceHelper.GetString(
                "AdminHubView_MaterialPriceInvalid",
                "Price per unit must be a non-negative number (use dot as decimal separator).");
            return false;
        }
        error = string.Empty;
        return true;
    }

    private void UpdateSimilarMaterials()
    {
        SimilarMaterials.Clear();
        var input = Name.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            OnPropertyChanged(nameof(HasSimilarMaterials));
            return;
        }

        var normalized = input.ToUpperInvariant();
        var candidates = _allMaterials
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Name) &&
                (!_editingMaterialId.HasValue || item.Id != _editingMaterialId.Value))
            .Select(item => new
            {
                Material = item,
                Score = Similarity(normalized, item.Name.Trim().ToUpperInvariant()),
                Contains =
                    item.Name.Contains(input, StringComparison.OrdinalIgnoreCase) ||
                    input.Contains(item.Name, StringComparison.OrdinalIgnoreCase)
            })
            .Where(item => item.Contains || item.Score >= 0.55d)
            .OrderByDescending(item => item.Contains)
            .ThenByDescending(item => item.Score)
            .ThenBy(item => item.Material.Name)
            .Take(3);
        var fallbackUnit = ResourceHelper.GetString(
            "AdminHubView_SimilarMaterials_NoUnit",
            "No unit specified");
        foreach (var candidate in candidates)
        {
            SimilarMaterials.Add(new SimilarMaterialSuggestion(
                candidate.Material.Name,
                string.IsNullOrWhiteSpace(candidate.Material.Unit)
                    ? fallbackUnit
                    : candidate.Material.Unit));
        }
        OnPropertyChanged(nameof(HasSimilarMaterials));
    }

    private static double Similarity(string first, string second)
    {
        var distance = LevenshteinDistance(first, second);
        var length = Math.Max(first.Length, second.Length);
        return length == 0 ? 1d : 1d - (double)distance / length;
    }

    private static int LevenshteinDistance(string first, string second)
    {
        var distances = new int[first.Length + 1, second.Length + 1];
        for (var index = 0; index <= first.Length; index++)
        {
            distances[index, 0] = index;
        }
        for (var index = 0; index <= second.Length; index++)
        {
            distances[0, index] = index;
        }
        for (var row = 1; row <= first.Length; row++)
        {
            for (var column = 1; column <= second.Length; column++)
            {
                var cost = first[row - 1] == second[column - 1] ? 0 : 1;
                distances[row, column] = Math.Min(
                    Math.Min(
                        distances[row - 1, column] + 1,
                        distances[row, column - 1] + 1),
                    distances[row - 1, column - 1] + cost);
            }
        }
        return distances[first.Length, second.Length];
    }
}
