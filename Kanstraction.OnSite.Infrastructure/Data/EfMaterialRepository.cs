using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.OnSite.Infrastructure.Data;

public sealed class EfMaterialRepository(OnSiteDbContext db) : IMaterialRepository
{
    public Task<bool> NameExistsAsync(
        string name,
        int? excludingMaterialId,
        CancellationToken cancellationToken = default)
        => db.Materials.AnyAsync(
            material =>
                EF.Functions.Collate(material.Name, "NOCASE") == name &&
                (!excludingMaterialId.HasValue || material.Id != excludingMaterialId.Value),
            cancellationToken);

    public Task<Material?> GetAsync(
        int materialId,
        CancellationToken cancellationToken = default)
        => db.Materials
            .Include(material => material.PriceHistory)
            .FirstOrDefaultAsync(material => material.Id == materialId, cancellationToken);

    public async Task UpdateActiveUsageDatesAsync(
        int materialId,
        DateTime usageDate,
        CancellationToken cancellationToken = default)
    {
        var usages = await db.MaterialUsages
            .Where(usage =>
                usage.MaterialId == materialId &&
                usage.SubStage.Status != WorkStatus.Finished &&
                usage.SubStage.Status != WorkStatus.Paid &&
                usage.SubStage.Status != WorkStatus.Stopped)
            .ToListAsync(cancellationToken);
        foreach (var usage in usages)
            usage.MoveUnfrozenUsageTo(usageDate);
    }

    public void Add(Material material)
        => db.Materials.Add(material);
}
