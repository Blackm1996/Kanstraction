using Kanstraction.OnSite.Application.BuildingTypes.Queries.ListBuildingTypesForCreation;
using System.Windows;

namespace Kanstraction.Views;

public partial class AddBuildingDialog : Window
{
    private readonly IReadOnlyList<BuildingTypeCreationOptionDto> _buildingTypes;

    public int? BuildingTypeId { get; private set; }
    public string BuildingCode { get; private set; } = string.Empty;

    public AddBuildingDialog(IReadOnlyList<BuildingTypeCreationOptionDto> buildingTypes)
    {
        InitializeComponent();
        _buildingTypes = buildingTypes;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CboType.ItemsSource = _buildingTypes;
        if (_buildingTypes.Count > 0)
            CboType.SelectedIndex = 0;
        TxtCode.Focus();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var code = TxtCode.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show(
                ResourceHelper.GetString("AddBuildingDialog_CodeRequired", "Enter a building code."),
                ResourceHelper.GetString("Common_RequiredTitle", "Required"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (CboType.SelectedValue is not int typeId)
        {
            MessageBox.Show(
                ResourceHelper.GetString("AddBuildingDialog_TypeRequired", "Select a building type."),
                ResourceHelper.GetString("Common_RequiredTitle", "Required"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var selectedType = _buildingTypes.First(type => type.Id == typeId);
        if (!selectedType.HasStagePresets)
        {
            var choice = MessageBox.Show(
                ResourceHelper.GetString("AddBuildingDialog_NoPresetWarning", "This building type has no stage presets assigned. Create anyway?"),
                ResourceHelper.GetString("AddBuildingDialog_NoPresetTitle", "No preset"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes)
                return;
        }

        if (selectedType.HasIncompleteDefaults)
        {
            var choice = MessageBox.Show(
                ResourceHelper.GetString("AddBuildingDialog_IncompleteDefaultsWarning", "Some labor costs or material quantities for this building type are missing. Continue anyway?"),
                ResourceHelper.GetString("AddBuildingDialog_IncompleteDefaultsTitle", "Missing defaults"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (choice != MessageBoxResult.Yes)
                return;
        }

        BuildingTypeId = typeId;
        BuildingCode = code;
        DialogResult = true;
    }
}
