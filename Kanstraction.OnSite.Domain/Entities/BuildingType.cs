namespace Kanstraction.OnSite.Domain.Entities;

public class BuildingType
{
    public int Id { get; internal set; }
    public string Name { get; internal set; } = string.Empty;
    public bool IsActive { get; internal set; } = true;

    internal BuildingType()
    {
    }

    public static BuildingType Create(string name, bool isActive)
    {
        var buildingType = new BuildingType();
        buildingType.Update(name, isActive);
        return buildingType;
    }

    public void Update(string name, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A building type name is required.", nameof(name));

        Name = name.Trim();
        IsActive = isActive;
    }
}
