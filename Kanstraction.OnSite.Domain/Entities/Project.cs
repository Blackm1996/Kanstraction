namespace Kanstraction.OnSite.Domain.Entities;

public class Project
{
    private readonly List<Building> _buildings = new();

    public int Id { get; internal set; }
    public string Name { get; internal set; } = "";
    public DateTime? StartDate { get; internal set; }
    public DateTime? EndDate { get; internal set; }

    public IReadOnlyCollection<Building> Buildings => _buildings;
    internal ICollection<Building> MutableBuildings => _buildings;

    internal Project()
    {
    }

    public static Project Create(string name, DateTime? startDate = null, DateTime? endDate = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A project name is required.", nameof(name));

        if (startDate.HasValue && endDate.HasValue && endDate.Value.Date < startDate.Value.Date)
            throw new ArgumentException("The project end date cannot precede its start date.", nameof(endDate));

        return new Project
        {
            Name = name.Trim(),
            StartDate = startDate?.Date,
            EndDate = endDate?.Date
        };
    }
}
