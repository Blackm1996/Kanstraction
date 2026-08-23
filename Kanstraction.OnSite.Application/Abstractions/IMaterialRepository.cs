using Kanstraction.OnSite.Domain.Entities;

namespace Kanstraction.OnSite.Application.Abstractions;

public interface IMaterialRepository
{
    Task<bool> NameExistsAsync(
        string name,
        int? excludingMaterialId,
        CancellationToken cancellationToken = default);
    Task<Material?> GetAsync(int materialId, CancellationToken cancellationToken = default);
    Task UpdateActiveUsageDatesAsync(
        int materialId,
        DateTime usageDate,
        CancellationToken cancellationToken = default);
    void Add(Material material);
}
