using Kanstraction.OnSite.Application.StagePresets.Commands.SaveStagePreset;
using Kanstraction.OnSite.Application.StagePresets.Queries.GetStagePresetEditor;
using Kanstraction.OnSite.Application.StagePresets.Queries.ListStagePresets;

namespace Kanstraction.OnSite.Application.Abstractions;

public interface IStagePresetRepository
{
    Task<IReadOnlyList<StagePresetListItemDto>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<StagePresetEditorDto?> GetEditorAsync(
        int stagePresetId,
        CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(
        string name,
        int? excludingStagePresetId,
        CancellationToken cancellationToken = default);

    Task<int> SaveAsync(
        StagePresetDefinition definition,
        CancellationToken cancellationToken = default);
}
