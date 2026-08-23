using Kanstraction.OnSite.Application.Abstractions;
using MediatR;

namespace Kanstraction.OnSite.Application.Materials.Queries.ListMaterialCategoriesForAdmin;

public sealed class ListMaterialCategoriesForAdminQueryHandler(IAdminReadRepository repository)
    : IRequestHandler<ListMaterialCategoriesForAdminQuery, IReadOnlyList<MaterialCategoryListItemDto>>
{
    public Task<IReadOnlyList<MaterialCategoryListItemDto>> Handle(ListMaterialCategoriesForAdminQuery request, CancellationToken cancellationToken)
        => repository.ListMaterialCategoriesAsync(cancellationToken);
}
