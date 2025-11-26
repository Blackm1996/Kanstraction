namespace Kanstraction.Domain.Entities;

public class Material
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal PricePerUnit { get; set; }
    public DateTime EffectiveSince { get; set; }
    public bool IsActive { get; set; } = true;
    public int MaterialCategoryId { get; set; }

    public MaterialCategory MaterialCategory { get; set; } = null!;

    public ICollection<MaterialPriceHistory> PriceHistory { get; set; } = new List<MaterialPriceHistory>();
}
