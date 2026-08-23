namespace Kanstraction.OnSite.Domain.Entities;

public class StagePreset
{
    private readonly List<SubStagePreset> _subStages = new();

    public int Id { get; internal set; }
    public string Name { get; internal set; } = string.Empty;
    public bool IsActive { get; internal set; } = true;
    public IReadOnlyCollection<SubStagePreset> SubStages => _subStages;
    internal ICollection<SubStagePreset> MutableSubStages => _subStages;

    internal StagePreset()
    {
    }

    public static StagePreset Create(string name, bool isActive)
    {
        var preset = new StagePreset();
        preset.Update(name, isActive);
        return preset;
    }

    public void Update(string name, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A stage preset name is required.", nameof(name));

        Name = name.Trim();
        IsActive = isActive;
    }
}
