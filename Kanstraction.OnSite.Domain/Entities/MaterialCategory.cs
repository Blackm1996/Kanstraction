namespace Kanstraction.OnSite.Domain.Entities;

public class MaterialCategory
{
    private readonly List<Material> _materials = new();

    public int Id { get; internal set; }
    public string Name { get; internal set; } = string.Empty;
    public IReadOnlyCollection<Material> Materials => _materials;
    internal ICollection<Material> MutableMaterials => _materials;

    internal MaterialCategory()
    {
    }

    public static MaterialCategory Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A material category name is required.", nameof(name));

        return new MaterialCategory { Name = name.Trim() };
    }
}
