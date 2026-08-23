using Kanstraction.OnSite.Application.Abstractions;
using MediatR;

namespace Kanstraction.OnSite.Application.Stages.Queries.GetStageAdditionOptions;

public sealed class GetStageAdditionOptionsQueryHandler(IOperationsReadRepository repository)
    : IRequestHandler<GetStageAdditionOptionsQuery, StageAdditionOptionsDto?>
{
    public Task<StageAdditionOptionsDto?> Handle(
        GetStageAdditionOptionsQuery request,
        CancellationToken cancellationToken)
        => repository.GetStageAdditionOptionsAsync(request.BuildingId, cancellationToken);
}
