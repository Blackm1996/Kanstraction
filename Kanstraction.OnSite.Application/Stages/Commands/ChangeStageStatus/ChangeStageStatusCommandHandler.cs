using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Stages.Commands.Common;
using Kanstraction.OnSite.Domain.Errors;
using Kanstraction.OnSite.Domain.Entities;
using MediatR;

namespace Kanstraction.OnSite.Application.Stages.Commands.ChangeStageStatus;

public sealed class ChangeStageStatusCommandHandler
    : IRequestHandler<ChangeStageStatusCommand, Result<WorkMutationResult>>
{
    private readonly IConstructionRepository _constructionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ChangeStageStatusCommandHandler(
        IConstructionRepository constructionRepository,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _constructionRepository = constructionRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<WorkMutationResult>> Handle(
        ChangeStageStatusCommand request,
        CancellationToken cancellationToken)
    {
        var building = await _constructionRepository.GetBuildingAggregateForStageStatusChangeAsync(request.StageId, cancellationToken);
        if (building == null)
            return Result<WorkMutationResult>.Failure(
                DomainErrorCodes.BuildingNotFound,
                "The building containing this stage was not found.");

        try
        {
            building.ChangeStageStatus(
                request.StageId,
                (WorkStatus)(int)request.NewStatus,
                _clock.Today);
        }
        catch (DomainRuleViolationException ex)
        {
            return Result<WorkMutationResult>.Failure(ex.Code, ex.Message);
        }

        await _unitOfWork.CommitAsync(cancellationToken);
        return Result<WorkMutationResult>.Success(
            new WorkMutationResult(building.Id, request.StageId));
    }
}
