using MediatR;

namespace Kanstraction.OnSite.Application.Materials.Queries.ListMaterialCategoriesForAdmin;

public sealed record MaterialCategoryListItemDto(int Id, string Name);
public sealed record ListMaterialCategoriesForAdminQuery() : IRequest<IReadOnlyList<MaterialCategoryListItemDto>>;
