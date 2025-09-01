namespace Kanstraction.Entities;
public class MaterialPriceHistory
{
    public int Id { get; set; }
    public int MaterialId { get; set; }
    public decimal PricePerUnit { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public Material Material { get; set; } = null!;
}
