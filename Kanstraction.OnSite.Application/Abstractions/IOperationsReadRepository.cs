using Kanstraction.OnSite.Application.Materials.Queries.ListMaterialUsages;
using Kanstraction.OnSite.Application.Materials.Queries.ListAvailableMaterials;
using Kanstraction.OnSite.Application.Stages.Queries.GetStageAdditionOptions;
using Kanstraction.OnSite.Application.Operations.Queries.GetBuildingWorkspace;
using Kanstraction.OnSite.Application.Reports.Queries.GetOperationsReportData;

namespace Kanstraction.OnSite.Application.Abstractions;

public interface IOperationsReadRepository
{
    Task<IReadOnlyList<MaterialUsageListItemDto>> ListMaterialUsagesAsync(
        int subStageId,
        bool freezePrices,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvailableMaterialDto>> ListAvailableMaterialsAsync(
        int subStageId,
        CancellationToken cancellationToken = default);
    Task<StageAdditionOptionsDto?> GetStageAdditionOptionsAsync(
        int buildingId,
        CancellationToken cancellationToken = default);
    Task<BuildingWorkspaceDto?> GetBuildingWorkspaceAsync(
        int buildingId,
        CancellationToken cancellationToken = default);
    Task<ProjectOperationsReportDataDto?> GetProjectReportDataAsync(
        int projectId,
        CancellationToken cancellationToken = default);
    Task<ReportStageDto?> GetStageReportDataAsync(
        int stageId,
        CancellationToken cancellationToken = default);
    Task<ReportSubStageDto?> GetSubStageReportDataAsync(
        int subStageId,
        CancellationToken cancellationToken = default);
}
