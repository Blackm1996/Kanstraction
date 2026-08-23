using MediatR;

namespace Kanstraction.OnSite.Application.BuildingTypes.Queries.ListBuildingTypesForCreation;

public sealed record BuildingTypeCreationOptionDto(
    int Id,
    string Name,
    bool HasStagePresets,
    bool HasIncompleteDefaults);

public sealed record ListBuildingTypesForCreationQuery()
    : IRequest<IReadOnlyList<BuildingTypeCreationOptionDto>>;
