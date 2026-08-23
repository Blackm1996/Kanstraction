using MediatR;

namespace Kanstraction.OnSite.Application.Materials.Queries.GetMaterialDetails;

public sealed record MaterialPriceHistoryDto(
    DateTime StartDate,
    DateTime? EndDate,
    decimal PricePerUnit);

public sealed record MaterialDetailsDto(
    int Id,
    string Name,
    string Unit,
    decimal PricePerUnit,
    bool IsActive,
    int MaterialCategoryId,
    string CategoryName,
    IReadOnlyList<MaterialPriceHistoryDto> PriceHistory);

public sealed record GetMaterialDetailsQuery(int MaterialId)
    : IRequest<MaterialDetailsDto?>;
