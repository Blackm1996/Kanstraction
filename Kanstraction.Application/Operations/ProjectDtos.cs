using Kanstraction.Domain.Entities;

namespace Kanstraction.Application.Operations;

public sealed record ProjectSummaryDto(int Id, string Name);

public sealed record MaterialUsageDto(
    int Id,
    string MaterialName,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal TotalCost,
    DateTime UsageDate,
    string? Notes);

public sealed record SubStageDto(
    int Id,
    string Name,
    WorkStatus Status,
    int OrderIndex,
    decimal LaborCost,
    DateTime? StartDate,
    DateTime? EndDate,
    IReadOnlyList<MaterialUsageDto> Materials);

public sealed record StageDto(
    int Id,
    string Name,
    WorkStatus Status,
    int OrderIndex,
    double Progress,
    IReadOnlyList<SubStageDto> SubStages);

public sealed record BuildingDto(
    int Id,
    string Code,
    string TypeName,
    WorkStatus Status,
    double Progress,
    IReadOnlyList<StageDto> Stages);

public sealed record ProjectDetailsDto(
    int Id,
    string Name,
    IReadOnlyList<BuildingDto> Buildings);

public interface IProjectCatalogService
{
    Task<IReadOnlyList<ProjectSummaryDto>> GetProjectsAsync(CancellationToken cancellationToken = default);

    Task<ProjectDetailsDto?> GetProjectAsync(int projectId, CancellationToken cancellationToken = default);
}
