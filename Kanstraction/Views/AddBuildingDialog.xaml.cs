using Kanstraction.Data;
using System.Windows;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Views;

public partial class AddBuildingDialog : Window
{
    private readonly AppDbContext _db;

    public int? BuildingTypeId { get; private set; }
    public string BuildingCode { get; private set; } = "";

    public AddBuildingDialog(AppDbContext db)
    {
        InitializeComponent();
        _db = db;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Load ACTIVE building types (ordered)
        var types = await _db.BuildingTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync();

        CboType.ItemsSource = types;
        if (types.Count > 0) CboType.SelectedIndex = 0;
        TxtCode.Focus();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var code = TxtCode.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show("Entrez un code de bâtiment.", "Obligatoire", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (CboType.SelectedValue is not int typeId)
        {
            MessageBox.Show("Sélectionnez un type de bâtiment.", "Obligatoire", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Optional (recommended): ensure code is unique within the current project.
        // We can't know the current ProjectId here; OperationsView will validate after dialog returns if needed.
        // If you want this dialog to receive a projectId for validation, pass it in ctor and check here.

        BuildingTypeId = typeId;
        BuildingCode = code;

        // quick sanity check: the chosen type must have at least 1 preset assigned (not required, but nice)
        var hasAnyPreset = await _db.BuildingTypeStagePresets.AnyAsync(x => x.BuildingTypeId == typeId);
        if (!hasAnyPreset)
        {
            var res = MessageBox.Show(
                "Ce type de bâtiment n'a aucun préréglage d'étape assigné. Créer quand même?",
                "Aucun préréglage", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
        }

        DialogResult = true;
    }
}