using System.Collections.Generic;

namespace Kanstraction.Domain.Entities;

public class MaterialCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<Material> Materials { get; set; } = new List<Material>();
}
