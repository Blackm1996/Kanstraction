using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.ViewModels.OnSite;

/// <summary>
/// Tracks lazy-loaded administration tabs, reference-data staleness, and
/// selection request versions independently from WPF controls.
/// </summary>
public sealed class AdministrationWorkspaceViewModel
{
    private readonly IOnSiteChangeFeed _changeFeed;
    private long _materialCategoriesRevision;
    private long _materialsRevision;
    private long _stagePresetsRevision;
    private long _buildingTypesRevision;
    private long _allDataRevision;
    private int _materialSelectionVersion;
    private int _buildingTypeSelectionVersion;

    public AdministrationWorkspaceViewModel(IOnSiteChangeFeed changeFeed)
    {
        _changeFeed = changeFeed;
        CaptureAllRevisions();
    }

    public bool IsViewLoaded { get; set; }
    public bool MaterialsLoaded { get; set; }
    public bool StagePresetsLoaded { get; set; }
    public bool BuildingTypesLoaded { get; set; }
    public bool StagePresetsStale { get; set; }
    public bool BuildingTypesStale { get; set; }

    public bool ApplyRevisionInvalidations()
    {
        var invalidateBuildingWorkspace = false;
        var allDataRevision = Revision(OnSiteChangeScope.AllData);
        if (allDataRevision != _allDataRevision)
        {
            MaterialsLoaded = false;
            StagePresetsLoaded = false;
            BuildingTypesLoaded = false;
            StagePresetsStale = false;
            BuildingTypesStale = false;
            invalidateBuildingWorkspace = true;
            _allDataRevision = allDataRevision;
        }

        var categoriesRevision = Revision(OnSiteChangeScope.MaterialCategories);
        var materialsRevision = Revision(OnSiteChangeScope.Materials);
        if (categoriesRevision != _materialCategoriesRevision ||
            materialsRevision != _materialsRevision)
        {
            MaterialsLoaded = false;
            StagePresetsStale = true;
            BuildingTypesStale = true;
            invalidateBuildingWorkspace = true;
        }

        var stagePresetsRevision = Revision(OnSiteChangeScope.StagePresets);
        if (stagePresetsRevision != _stagePresetsRevision)
        {
            StagePresetsLoaded = false;
            BuildingTypesStale = true;
            invalidateBuildingWorkspace = true;
        }

        if (Revision(OnSiteChangeScope.BuildingTypes) != _buildingTypesRevision)
        {
            BuildingTypesLoaded = false;
        }

        return invalidateBuildingWorkspace;
    }

    public void InvalidateAllTabs()
    {
        MaterialsLoaded = false;
        StagePresetsLoaded = false;
        BuildingTypesLoaded = false;
        StagePresetsStale = false;
        BuildingTypesStale = false;
        InvalidateSelections();
    }

    public void CaptureMaterialRevisions()
    {
        _materialCategoriesRevision = Revision(OnSiteChangeScope.MaterialCategories);
        _materialsRevision = Revision(OnSiteChangeScope.Materials);
    }

    public void CaptureBuildingReferenceRevisions()
    {
        _materialsRevision = Revision(OnSiteChangeScope.Materials);
        _stagePresetsRevision = Revision(OnSiteChangeScope.StagePresets);
        _buildingTypesRevision = Revision(OnSiteChangeScope.BuildingTypes);
    }

    public void CaptureStagePresetsRevision() =>
        _stagePresetsRevision = Revision(OnSiteChangeScope.StagePresets);

    public void CaptureBuildingTypesRevision() =>
        _buildingTypesRevision = Revision(OnSiteChangeScope.BuildingTypes);

    public void CaptureAllRevisions()
    {
        CaptureMaterialRevisions();
        CaptureBuildingReferenceRevisions();
        _allDataRevision = Revision(OnSiteChangeScope.AllData);
    }

    public int BeginMaterialSelection() => ++_materialSelectionVersion;
    public int BeginBuildingTypeSelection() => ++_buildingTypeSelectionVersion;
    public bool IsCurrentMaterialSelection(int version) => version == _materialSelectionVersion;
    public bool IsCurrentBuildingTypeSelection(int version) => version == _buildingTypeSelectionVersion;
    public void InvalidateMaterialSelection() => _materialSelectionVersion++;
    public void InvalidateBuildingTypeSelection() => _buildingTypeSelectionVersion++;

    private void InvalidateSelections()
    {
        InvalidateMaterialSelection();
        InvalidateBuildingTypeSelection();
    }

    private long Revision(OnSiteChangeScope scope) => _changeFeed.GetRevision(scope);
}
