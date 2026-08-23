using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Stages.Commands.Common;
using Kanstraction.OnSite.Domain.Errors;
using MediatR;

namespace Kanstraction.OnSite.Application.Stages.Commands.AddStageFromPreset;

public sealed class AddStageFromPresetCommandHandler(
    IConstructionRepository repository,
    IBuildingTemplateReader templateReader,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<AddStageFromPresetCommand, Result<WorkMutationResult>>
{
    public async Task<Result<WorkMutationResult>> Handle(
        AddStageFromPresetCommand request,
        CancellationToken cancellationToken)
    {
        var building = await repository.GetBuildingAggregateAsync(
            request.BuildingId,
            cancellationToken);
        if (building == null)
            return Result<WorkMutationResult>.Failure(DomainErrorCodes.BuildingNotFound, "The building was not found.");

        var template = await templateReader.GetStageAsync(
            request.BuildingTypeId,
            request.StagePresetId,
            cancellationToken);
        if (template == null)
            return Result<WorkMutationResult>.Failure("stage.preset_not_found", "The stage preset was not found.");

        try
        {
            var stage = building.InsertStage(template.Name, request.OrderIndex);
            foreach (var subStageTemplate in template.SubStages.OrderBy(item => item.OrderIndex))
            {
                var subStage = stage.AddSubStage(
                    subStageTemplate.Name,
                    subStageTemplate.OrderIndex,
                    subStageTemplate.LaborCost);
                foreach (var material in subStageTemplate.Materials)
                    subStage.AddMaterialUsage(material.MaterialId, material.Quantity, clock.Today);
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return Result<WorkMutationResult>.Success(
                new WorkMutationResult(building.Id, stage.Id));
        }
        catch (DomainRuleViolationException ex)
        {
            return Result<WorkMutationResult>.Failure(ex.Code, ex.Message);
        }
    }
}
