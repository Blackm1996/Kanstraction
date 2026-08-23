using Kanstraction.OnSite.Application.Abstractions;
using MediatR;

namespace Kanstraction.OnSite.Application.Materials.Queries.GetMaterialDetails;

public sealed class GetMaterialDetailsQueryHandler(IAdminReadRepository repository)
    : IRequestHandler<GetMaterialDetailsQuery, MaterialDetailsDto?>
{
    public Task<MaterialDetailsDto?> Handle(
        GetMaterialDetailsQuery request,
        CancellationToken cancellationToken)
        => repository.GetMaterialDetailsAsync(request.MaterialId, cancellationToken);
}
