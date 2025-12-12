namespace Kanstraction.Domain.Entities;

public class BuildingType { 
    public int Id { get; set; } 
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
