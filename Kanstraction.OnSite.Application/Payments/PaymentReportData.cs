namespace Kanstraction.OnSite.Application.Payments;

public sealed record PaymentReportData(
    string ProjectName,
    DateTime GeneratedAt,
    IReadOnlyList<PaymentBuildingGroup> Buildings,
    decimal GrandTotal);

public sealed record PaymentBuildingGroup(
    string BuildingCode,
    IReadOnlyList<PaymentStageGroup> Stages,
    decimal BuildingSubtotal);

public sealed record PaymentStageGroup(
    string StageName,
    IReadOnlyList<PaymentSubStageRow> Items,
    decimal StageSubtotal);

public sealed record PaymentSubStageRow(
    string StageName,
    string SubStageName,
    decimal LaborCost);
