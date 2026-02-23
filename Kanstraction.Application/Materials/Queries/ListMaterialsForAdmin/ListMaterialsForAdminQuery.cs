using MediatR;

namespace Kanstraction.Application.Materials.Queries.ListMaterialsForAdmin;

public sealed record MaterialListItemDto(
    int Id,
    string Name,
    string Unit,
    bool IsActive,
    decimal PricePerUnit,
    int MaterialCategoryId,
    string CategoryName);

public sealed record ListMaterialsForAdminQuery() : IRequest<IReadOnlyList<MaterialListItemDto>>;
