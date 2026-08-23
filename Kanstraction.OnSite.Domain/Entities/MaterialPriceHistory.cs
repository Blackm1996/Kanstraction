namespace Kanstraction.OnSite.Domain.Entities;
public class MaterialPriceHistory
{
    public int Id { get; internal set; }
    public int MaterialId { get; internal set; }
    public decimal PricePerUnit { get; internal set; }
    public DateTime StartDate { get; internal set; }
    public DateTime? EndDate { get; internal set; }

    public Material Material { get; internal set; } = null!;

    internal MaterialPriceHistory()
    {
    }

    public static MaterialPriceHistory Create(
        int materialId,
        decimal pricePerUnit,
        DateTime startDate,
        DateTime? endDate = null)
    {
        if (pricePerUnit < 0)
            throw new ArgumentOutOfRangeException(nameof(pricePerUnit));
        if (endDate.HasValue && endDate.Value.Date < startDate.Date)
            throw new ArgumentException("The price end date cannot precede its start date.", nameof(endDate));

        return new MaterialPriceHistory
        {
            MaterialId = materialId,
            PricePerUnit = pricePerUnit,
            StartDate = startDate.Date,
            EndDate = endDate?.Date
        };
    }
}
