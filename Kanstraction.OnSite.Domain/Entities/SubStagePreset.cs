namespace Kanstraction.OnSite.Domain.Entities;

public class SubStagePreset
{
    public int Id { get; internal set; }
    public int StagePresetId { get; internal set; }
    public string Name { get; internal set; } = string.Empty;
    public int OrderIndex { get; internal set; }
    public StagePreset StagePreset { get; internal set; } = null!;

    internal SubStagePreset()
    {
    }

    public static SubStagePreset Create(int stagePresetId, string name, int orderIndex)
    {
        var preset = new SubStagePreset { StagePresetId = stagePresetId };
        preset.Update(name, orderIndex);
        return preset;
    }

    public void Update(string name, int orderIndex)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A sub-stage preset name is required.", nameof(name));
        if (orderIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(orderIndex));

        Name = name.Trim();
        OrderIndex = orderIndex;
    }
}
