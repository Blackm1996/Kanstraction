using Kanstraction.Application.Abstractions;
using MediatR;

namespace Kanstraction.Application.Materials.Queries.ListMaterialCategoriesForAdmin;

public sealed class ListMaterialCategoriesForAdminQueryHandler(IAdminReadRepository repository)
    : IRequestHandler<ListMaterialCategoriesForAdminQuery, IReadOnlyList<MaterialCategoryListItemDto>>
{
    public Task<IReadOnlyList<MaterialCategoryListItemDto>> Handle(ListMaterialCategoriesForAdminQuery request, CancellationToken cancellationToken)
        => repository.ListMaterialCategoriesAsync(cancellationToken);
}
