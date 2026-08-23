using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Materials.Queries.ListAvailableMaterials;
using MediatR;

namespace Kanstraction.OnSite.Application.StagePresets.Queries.GetStagePresetEditor;

public sealed class GetStagePresetEditorQueryHandler(
    IStagePresetRepository repository,
    IAdminReadRepository adminReadRepository)
    : IRequestHandler<GetStagePresetEditorQuery, StagePresetEditorWorkspaceDto?>
{
    public async Task<StagePresetEditorWorkspaceDto?> Handle(
        GetStagePresetEditorQuery request,
        CancellationToken cancellationToken)
    {
        var activeMaterials = (await adminReadRepository
                .ListMaterialsAsync(cancellationToken))
            .Where(material => material.IsActive)
            .OrderBy(material => material.CategoryName)
            .ThenBy(material => material.Name)
            .Select(material => new AvailableMaterialDto(
                material.Id,
                material.Name,
                material.Unit,
                material.CategoryName))
            .ToList();

        if (!request.StagePresetId.HasValue)
            return new StagePresetEditorWorkspaceDto(null, activeMaterials);

        var preset = await repository.GetEditorAsync(
            request.StagePresetId.Value,
            cancellationToken);
        return preset == null
            ? null
            : new StagePresetEditorWorkspaceDto(preset, activeMaterials);
    }
}
