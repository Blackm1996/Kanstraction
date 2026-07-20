using MediatR;

namespace Kanstraction.Application.BuildingTypes.Queries.ListBuildingTypesForAdmin;

public sealed record BuildingTypeListItemDto(int Id, string Name, bool IsActive);
public sealed record ListBuildingTypesForAdminQuery() : IRequest<IReadOnlyList<BuildingTypeListItemDto>>;
