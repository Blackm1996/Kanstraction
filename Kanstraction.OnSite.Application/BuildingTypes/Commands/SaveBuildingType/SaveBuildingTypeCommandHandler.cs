using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using MediatR;

namespace Kanstraction.OnSite.Application.BuildingTypes.Commands.SaveBuildingType;

public sealed class SaveBuildingTypeCommandHandler(
    IBuildingTypeRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SaveBuildingTypeCommand, Result<int>>
{
    public async Task<Result<int>> Handle(
        SaveBuildingTypeCommand request,
        CancellationToken cancellationToken)
    {
        var definition = request.Definition;
        var name = definition.Name.Trim();
        if (name.Length == 0)
        {
            return Result<int>.Failure(
                "building_type.name_required",
                "A building type name is required.");
        }

        if (definition.Stages
            .GroupBy(stage => stage.StagePresetId)
            .Any(group => group.Key <= 0 || group.Count() > 1))
        {
            return Result<int>.Failure(
                "building_type.invalid_stage_assignments",
                "Stage assignments must be valid and unique.");
        }

        foreach (var stage in definition.Stages)
        {
            if (stage.SubStages
                .GroupBy(subStage => subStage.SubStagePresetId)
                .Any(group => group.Key <= 0 || group.Count() > 1))
            {
                return Result<int>.Failure(
                    "building_type.invalid_sub_stage_assignments",
                    "Sub-stage assignments must be valid and unique.");
            }

            if (stage.SubStages
                .SelectMany(subStage => subStage.Materials)
                .Any(material => material.MaterialId <= 0 || material.Quantity < 0))
            {
                return Result<int>.Failure(
                    "building_type.invalid_material_quantity",
                    "Material quantities cannot be negative.");
            }

            if (stage.SubStages.Any(subStage => subStage.LaborCost < 0))
            {
                return Result<int>.Failure(
                    "building_type.invalid_labor_cost",
                    "Labor costs cannot be negative.");
            }
        }

        if (await repository.NameExistsAsync(
                name,
                definition.Id,
                cancellationToken))
        {
            return Result<int>.Failure(
                "building_type.duplicate_name",
                "A building type with this name already exists.");
        }

        var normalized = definition with
        {
            Name = name,
            Stages = definition.Stages
                .Select((stage, index) => stage with { OrderIndex = index + 1 })
                .ToList()
        };

        try
        {
            return Result<int>.Success(
                await unitOfWork.ExecuteInTransactionAsync(
                    token => repository.SaveAsync(normalized, token),
                    cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return Result<int>.Failure("building_type.invalid_state", ex.Message);
        }
    }
}
