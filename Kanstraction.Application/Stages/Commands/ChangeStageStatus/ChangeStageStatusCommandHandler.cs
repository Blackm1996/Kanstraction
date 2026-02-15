using Kanstraction.Application.Abstractions;
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
        var building = await _constructionRepository.GetBuildingAggregateForStageStatusChangeAsync(request.StageId, cancellationToken);
        if (building == null)
            return;

        building.ChangeStageStatus(request.StageId, request.NewStatus, DateTime.Today);

        await _constructionRepository.SaveBuildingAggregateAsync(building, cancellationToken);
    }
}
