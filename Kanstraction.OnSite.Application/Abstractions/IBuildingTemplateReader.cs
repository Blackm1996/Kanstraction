using Kanstraction.OnSite.Application.Buildings;

namespace Kanstraction.OnSite.Application.Abstractions;

public interface IBuildingTemplateReader
{
    Task<BuildingTemplateDefinition?> GetAsync(
        int buildingTypeId,
        CancellationToken cancellationToken = default);
    async Task<BuildingStageTemplateDefinition?> GetStageAsync(
        int buildingTypeId,
        int stagePresetId,
        CancellationToken cancellationToken = default)
    {
        var template = await GetAsync(buildingTypeId, cancellationToken);
        return template?.Stages.FirstOrDefault(stage => stage.StagePresetId == stagePresetId);
    }
}
