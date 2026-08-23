using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Stages.Commands.Common;
using Kanstraction.OnSite.Domain.Errors;
using MediatR;

namespace Kanstraction.OnSite.Application.SubStages.Commands.ChangeSubStageStatus;

public sealed class ChangeSubStageStatusCommandHandler
    : IRequestHandler<ChangeSubStageStatusCommand, Result<WorkMutationResult>>
{
    private readonly IConstructionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ChangeSubStageStatusCommandHandler(
        IConstructionRepository repository,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<WorkMutationResult>> Handle(
        ChangeSubStageStatusCommand request,
        CancellationToken cancellationToken)
    {
        var building = await _repository.GetBuildingAggregateForSubStageAsync(
            request.SubStageId,
            cancellationToken);

        if (building == null)
        {
            return Result<WorkMutationResult>.Failure(
                DomainErrorCodes.BuildingNotFound,
                "The building containing this sub-stage was not found.");
        }

        try
        {
            switch (request.Action)
            {
                case SubStageStatusAction.Start:
                    building.StartSubStage(request.SubStageId, _clock.Today);
                    break;
                case SubStageStatusAction.Finish:
                    building.FinishSubStage(request.SubStageId, _clock.Today);
                    break;
                case SubStageStatusAction.Reset:
                    building.ResetSubStage(request.SubStageId, _clock.Today);
                    break;
                default:
                    return Result<WorkMutationResult>.Failure(
                        "sub_stage.unsupported_action",
                        "The requested sub-stage action is not supported.");
            }
        }
        catch (DomainRuleViolationException ex)
        {
            return Result<WorkMutationResult>.Failure(ex.Code, ex.Message);
        }

        await _unitOfWork.CommitAsync(cancellationToken);

        var stageId = building.Stages
            .First(stage => stage.SubStages.Any(subStage => subStage.Id == request.SubStageId))
            .Id;

        return Result<WorkMutationResult>.Success(
            new WorkMutationResult(building.Id, stageId, request.SubStageId));
    }
}
