using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.OnSite.Application.Buildings.Commands.StopBuilding;

public sealed record StopBuildingCommand(int BuildingId)
    : IStateChangingRequest<Result<CompensationReportData>>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result<CompensationReportData> response)
        => response.Value is { } report
            ? [new(
                OnSiteChangeScope.ProjectWork,
                OnSiteChangeKind.Updated,
                BuildingId: report.BuildingId,
                EntityId: report.BuildingId)]
            : [];
}

public sealed record CompensationReportData(
    int BuildingId,
    string BuildingCode,
    IReadOnlyList<CompensationReportEntry> Entries);

public sealed record CompensationReportEntry(
    string StageName,
    string SubStageName,
    int StageOrder,
    int SubStageOrder,
    decimal LaborCost,
    decimal MaterialCost);
