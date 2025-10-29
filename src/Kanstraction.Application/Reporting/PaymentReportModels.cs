using System;
using System.Collections.Generic;

namespace Kanstraction.Application.Reporting;

public sealed class PaymentReportSubStageRow
{
    public string StageName { get; set; } = string.Empty;
    public string SubStageName { get; set; } = string.Empty;
    public decimal LaborCost { get; set; }
}

public sealed class PaymentReportStageGroup
{
    public string StageName { get; set; } = string.Empty;
    public List<PaymentReportSubStageRow> Items { get; set; } = new();
    public decimal StageSubtotal { get; set; }
}

public sealed class PaymentReportBuildingGroup
{
    public string BuildingCode { get; set; } = string.Empty;
    public List<PaymentReportStageGroup> Stages { get; set; } = new();
    public decimal BuildingSubtotal { get; set; }
}

public sealed class PaymentReportData
{
    public string ProjectName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public List<PaymentReportBuildingGroup> Buildings { get; set; } = new();
    public decimal GrandTotal { get; set; }
}
