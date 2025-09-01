namespace Kanstraction.Entities;

public class MaterialUsage
{
    public int Id { get; set; }
    public int SubStageId { get; set; }
    public int MaterialId { get; set; }
    public decimal Qty { get; set; }
    public DateTime UsageDate { get; set; }
    public string? Notes { get; set; }

    public SubStage SubStage { get; set; } = null!;
    public Material Material { get; set; } = null!;
}
