namespace Kanstraction.OnSite.Application.Buildings;

public sealed record BuildingTemplateDefinition(
    int BuildingTypeId,
    IReadOnlyList<BuildingStageTemplateDefinition> Stages);

public sealed record BuildingStageTemplateDefinition(
    int StagePresetId,
    string Name,
    int OrderIndex,
    IReadOnlyList<BuildingSubStageTemplateDefinition> SubStages);

public sealed record BuildingSubStageTemplateDefinition(
    string Name,
    int OrderIndex,
    decimal LaborCost,
    IReadOnlyList<BuildingMaterialTemplateDefinition> Materials);

public sealed record BuildingMaterialTemplateDefinition(int MaterialId, decimal Quantity);
