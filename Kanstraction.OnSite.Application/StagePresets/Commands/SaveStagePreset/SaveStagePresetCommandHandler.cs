using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using MediatR;

namespace Kanstraction.OnSite.Application.StagePresets.Commands.SaveStagePreset;

public sealed class SaveStagePresetCommandHandler(
    IStagePresetRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SaveStagePresetCommand, Result<int>>
{
    public async Task<Result<int>> Handle(
        SaveStagePresetCommand request,
        CancellationToken cancellationToken)
    {
        var definition = request.Definition;
        var name = definition.Name.Trim();
        if (name.Length == 0)
        {
            return Result<int>.Failure(
                "stage_preset.name_required",
                "Preset name is required.");
        }

        if (await repository.NameExistsAsync(
                name,
                definition.Id,
                cancellationToken))
        {
            return Result<int>.Failure(
                "stage_preset.duplicate_name",
                "A stage preset with this name already exists.");
        }

        var normalizedSubStages = new List<StagePresetSubStageDefinition>();
        foreach (var (subStage, index) in definition.SubStages.Select((value, index) => (value, index)))
        {
            var subStageName = subStage.Name.Trim();
            if (subStageName.Length == 0)
            {
                return Result<int>.Failure(
                    "stage_preset.sub_stage_name_required",
                    "Each sub-stage must have a name.");
            }

            if (subStage.Materials.Any(material => material.MaterialId <= 0))
            {
                return Result<int>.Failure(
                    "stage_preset.invalid_material",
                    "Every material assignment must reference a valid material.");
            }

            if (subStage.Materials
                .GroupBy(material => material.MaterialId)
                .Any(group => group.Count() > 1))
            {
                return Result<int>.Failure(
                    "stage_preset.duplicate_material",
                    $"A material is listed more than once in sub-stage \"{subStageName}\".");
            }

            normalizedSubStages.Add(subStage with
            {
                Name = subStageName,
                OrderIndex = index + 1
            });
        }

        var normalized = definition with
        {
            Name = name,
            SubStages = normalizedSubStages
        };

        try
        {
            var id = await unitOfWork.ExecuteInTransactionAsync(
                token => repository.SaveAsync(normalized, token),
                cancellationToken);
            return Result<int>.Success(id);
        }
        catch (InvalidOperationException ex)
        {
            return Result<int>.Failure("stage_preset.invalid_state", ex.Message);
        }
    }
}
