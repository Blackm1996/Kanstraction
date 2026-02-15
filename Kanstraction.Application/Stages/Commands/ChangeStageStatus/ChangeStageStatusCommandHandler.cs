using Kanstraction.Application.Abstractions;
using Kanstraction.Domain.Entities;
using MediatR;

namespace Kanstraction.Application.Stages.Commands.ChangeStageStatus;

public sealed class ChangeStageStatusCommandHandler : IRequestHandler<ChangeStageStatusCommand>
{
    private readonly IConstructionRepository _constructionRepository;

    public ChangeStageStatusCommandHandler(IConstructionRepository constructionRepository)
    {
        _constructionRepository = constructionRepository;
    }

    public async Task Handle(ChangeStageStatusCommand request, CancellationToken cancellationToken)
    {
        var stage = await _constructionRepository.GetStageForStatusChangeAsync(request.StageId, cancellationToken);
        if (stage == null)
            return;

        var today = DateTime.Today;

        if (request.NewStatus == WorkStatus.Stopped)
        {
            foreach (var buildingStage in stage.Building.Stages)
                buildingStage.ApplyStatusTransition(WorkStatus.Stopped, today);

            stage.Building.Status = WorkStatus.Stopped;
        }
        else
        {
            stage.ApplyStatusTransition(request.NewStatus, today);
            stage.Building.RecomputeStatusFromStages();
        }

        await _constructionRepository.SaveChangesAsync(cancellationToken);
    }
}
