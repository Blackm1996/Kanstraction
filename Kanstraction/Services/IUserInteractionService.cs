using Kanstraction.OnSite.Application.BuildingTypes.Queries.ListBuildingTypesForCreation;
using Kanstraction.OnSite.Application.Materials.Queries.ListAvailableMaterials;
using Kanstraction.OnSite.Application.Stages.Queries.GetStageAdditionOptions;

namespace Kanstraction.Services;

public sealed record BuildingDraft(int BuildingTypeId, string Code);
public sealed record MaterialUsageDraft(int MaterialId, decimal Quantity);
public sealed record SubStageDraft(string Name, decimal LaborCost, int OrderIndex);
public sealed record StageDraft(int StagePresetId, int OrderIndex);
public sealed record ReportPeriod(DateTime StartDate, DateTime EndDate);

public interface IUserInteractionService
{
    void Information(string message, string title);
    void Warning(string message, string title);
    void Error(string message, string title);
    bool Confirm(string message, string title, bool isWarning = false);
    bool ConfirmValueChange(string title, string message, string confirmText, string revertText);
    string? PromptText(string title, string? initialValue = null);
    BuildingDraft? CreateBuilding(IReadOnlyList<BuildingTypeCreationOptionDto> buildingTypes);
    MaterialUsageDraft? CreateMaterialUsage(IReadOnlyList<AvailableMaterialDto> materials);
    SubStageDraft? CreateSubStage(int currentCount);
    StageDraft? CreateStage(IReadOnlyList<StagePresetOptionDto> presets, int currentCount);
    ReportPeriod? SelectReportPeriod();
    string? SelectSavePath(string title, string filter, string defaultFileName);
    string? SelectOpenPath(string title, string filter);
    void OpenFile(string path);
}
