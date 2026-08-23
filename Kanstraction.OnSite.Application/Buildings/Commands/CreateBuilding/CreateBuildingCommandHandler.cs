using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Domain.Errors;
using MediatR;

namespace Kanstraction.OnSite.Application.Buildings.Commands.CreateBuilding;

public sealed class CreateBuildingCommandHandler
    : IRequestHandler<CreateBuildingCommand, Result<CreateBuildingResult>>
{
    private readonly IConstructionRepository _repository;
    private readonly IBuildingTemplateReader _templateReader;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateBuildingCommandHandler(
        IConstructionRepository repository,
        IBuildingTemplateReader templateReader,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _repository = repository;
        _templateReader = templateReader;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<CreateBuildingResult>> Handle(
        CreateBuildingCommand request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        if (code.Length == 0)
            return Result<CreateBuildingResult>.Failure(
                "building.code_required",
                "A building code is required.");

        if (await _repository.BuildingCodeExistsAsync(
                request.ProjectId,
                code,
                cancellationToken))
        {
            return Result<CreateBuildingResult>.Failure(
                DomainErrorCodes.DuplicateBuildingCode,
                "A building with this code already exists in this project.");
        }

        var template = await _templateReader.GetAsync(
            request.BuildingTypeId,
            cancellationToken);

        if (template == null)
            return Result<CreateBuildingResult>.Failure(
                "building.type_not_found",
                "The selected building type was not found.");

        try
        {
            var building = Domain.Entities.Building.Create(
                request.ProjectId,
                request.BuildingTypeId,
                code);

            foreach (var stageTemplate in template.Stages.OrderBy(stage => stage.OrderIndex))
            {
                var stage = building.AddStage(
                    stageTemplate.Name,
                    stageTemplate.OrderIndex);

                foreach (var subStageTemplate in stageTemplate.SubStages.OrderBy(subStage => subStage.OrderIndex))
                {
                    var subStage = stage.AddSubStage(
                        subStageTemplate.Name,
                        subStageTemplate.OrderIndex,
                        subStageTemplate.LaborCost);

                    foreach (var material in subStageTemplate.Materials)
                        subStage.AddMaterialUsage(
                            material.MaterialId,
                            material.Quantity,
                            _clock.Today);
                }
            }

            _repository.Add(building);
            await _unitOfWork.CommitAsync(cancellationToken);
            return Result<CreateBuildingResult>.Success(new CreateBuildingResult(building.Id));
        }
        catch (DomainRuleViolationException ex)
        {
            return Result<CreateBuildingResult>.Failure(ex.Code, ex.Message);
        }
    }
}
