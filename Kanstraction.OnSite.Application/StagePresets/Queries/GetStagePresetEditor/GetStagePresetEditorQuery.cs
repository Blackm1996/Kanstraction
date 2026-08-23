using Kanstraction.OnSite.Application.Materials.Queries.ListAvailableMaterials;
using MediatR;

namespace Kanstraction.OnSite.Application.StagePresets.Queries.GetStagePresetEditor;

public sealed record StagePresetMaterialDto(
    int? UsageId,
    int MaterialId,
    string MaterialName,
    string CategoryName,
    string Unit);

public sealed record StagePresetSubStageDto(
    int? Id,
    string Name,
    int OrderIndex,
    IReadOnlyList<StagePresetMaterialDto> Materials);

public sealed record StagePresetEditorDto(
    int Id,
    string Name,
    bool IsActive,
    IReadOnlyList<StagePresetSubStageDto> SubStages);

public sealed record StagePresetEditorWorkspaceDto(
    StagePresetEditorDto? Preset,
    IReadOnlyList<AvailableMaterialDto> ActiveMaterials);

public sealed record GetStagePresetEditorQuery(int? StagePresetId)
    : IRequest<StagePresetEditorWorkspaceDto?>;
