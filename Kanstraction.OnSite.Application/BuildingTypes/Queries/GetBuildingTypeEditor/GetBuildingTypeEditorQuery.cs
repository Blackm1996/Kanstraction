using MediatR;

namespace Kanstraction.OnSite.Application.BuildingTypes.Queries.GetBuildingTypeEditor;

public sealed record BuildingTypeMaterialEditorDto(
    int MaterialId,
    string MaterialName,
    string Unit,
    decimal? Quantity);

public sealed record BuildingTypeSubStageEditorDto(
    int Id,
    string Name,
    int OrderIndex,
    decimal? LaborCost,
    bool IncludeInReport,
    IReadOnlyList<BuildingTypeMaterialEditorDto> Materials);

public sealed record BuildingTypeStageEditorDto(
    int StagePresetId,
    string Name,
    int OrderIndex,
    IReadOnlyList<BuildingTypeSubStageEditorDto> SubStages);

public sealed record BuildingTypeEditorDto(
    int Id,
    string Name,
    bool IsActive,
    IReadOnlyList<BuildingTypeStageEditorDto> AssignedStages);

public sealed record BuildingTypeEditorWorkspaceDto(
    BuildingTypeEditorDto? BuildingType,
    IReadOnlyList<BuildingTypeStageEditorDto> StageTemplates);

public sealed record GetBuildingTypeEditorQuery(int? BuildingTypeId)
    : IRequest<BuildingTypeEditorWorkspaceDto?>;
