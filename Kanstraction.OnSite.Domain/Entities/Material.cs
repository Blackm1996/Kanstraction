namespace Kanstraction.OnSite.Domain.Entities;

public class Material
{
    private readonly List<MaterialPriceHistory> _priceHistory = new();

    public int Id { get; internal set; }
    public string Name { get; internal set; } = "";
    public string Unit { get; internal set; } = "";
    public decimal PricePerUnit { get; internal set; }
    public DateTime EffectiveSince { get; internal set; }
    public bool IsActive { get; internal set; } = true;
    public int MaterialCategoryId { get; internal set; }

    public MaterialCategory MaterialCategory { get; internal set; } = null!;

    public IReadOnlyCollection<MaterialPriceHistory> PriceHistory => _priceHistory;
    internal ICollection<MaterialPriceHistory> MutablePriceHistory => _priceHistory;

    internal Material()
    {
    }

    public static Material Create(
        string name,
        string unit,
        decimal pricePerUnit,
        DateTime effectiveSince,
        bool isActive,
        int categoryId)
    {
        Validate(name, pricePerUnit);
        var material = new Material
        {
            Name = name.Trim(),
            Unit = unit.Trim(),
            PricePerUnit = pricePerUnit,
            EffectiveSince = effectiveSince,
            IsActive = isActive,
            MaterialCategoryId = categoryId
        };
        material._priceHistory.Add(new MaterialPriceHistory
        {
            Material = material,
            PricePerUnit = pricePerUnit,
            StartDate = effectiveSince
        });
        return material;
    }

    public void Update(
        string name,
        string unit,
        decimal pricePerUnit,
        DateTime effectiveSince,
        bool isActive,
        int categoryId)
    {
        Validate(name, pricePerUnit);
        var priceChanged = PricePerUnit != pricePerUnit;
        var dateChanged = EffectiveSince.Date != effectiveSince.Date;

        Name = name.Trim();
        Unit = unit.Trim();
        IsActive = isActive;
        MaterialCategoryId = categoryId;
        PricePerUnit = pricePerUnit;
        EffectiveSince = effectiveSince;

        if (!priceChanged)
            return;

        var open = PriceHistory
            .Where(history => !history.EndDate.HasValue)
            .OrderByDescending(history => history.StartDate)
            .FirstOrDefault();

        if (!dateChanged && open != null)
        {
            open.PricePerUnit = pricePerUnit;
            return;
        }

        if (open != null)
        {
            var closeDate = effectiveSince.AddDays(-1);
            open.EndDate = closeDate < open.StartDate ? open.StartDate : closeDate;
        }

        _priceHistory.Add(new MaterialPriceHistory
        {
            Material = this,
            PricePerUnit = pricePerUnit,
            StartDate = effectiveSince
        });
    }

    private static void Validate(string name, decimal pricePerUnit)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                "material.name_required",
                "A material name is required.");
        if (pricePerUnit < 0)
            throw new Kanstraction.OnSite.Domain.Errors.DomainRuleViolationException(
                "material.price_negative",
                "Price cannot be negative.");
    }
}
