namespace Kanstraction.OnSite.Domain.Entities;

public class BuildingTypeSubStageLabor
{
    public int BuildingTypeId { get; internal set; }
    public int SubStagePresetId { get; internal set; }
    public decimal? LaborCost { get; internal set; }
    public bool IncludeInReport { get; internal set; }
    public BuildingType BuildingType { get; internal set; } = null!;
    public SubStagePreset SubStagePreset { get; internal set; } = null!;

    internal BuildingTypeSubStageLabor()
    {
    }

    public static BuildingTypeSubStageLabor Create(
        int buildingTypeId,
        int subStagePresetId,
        decimal? laborCost,
        bool includeInReport)
    {
        var labor = new BuildingTypeSubStageLabor
        {
            BuildingTypeId = buildingTypeId,
            SubStagePresetId = subStagePresetId
        };
        labor.Update(laborCost, includeInReport);
        return labor;
    }

    public void Update(decimal? laborCost, bool includeInReport)
    {
        if (laborCost < 0)
            throw new ArgumentOutOfRangeException(nameof(laborCost));

        LaborCost = laborCost;
        IncludeInReport = includeInReport;
    }
}
