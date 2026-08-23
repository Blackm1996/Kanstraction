using Kanstraction.OnSite.Domain.Entities;

namespace Kanstraction.OnSite.Application.Abstractions;

public interface IMaterialCategoryRepository
{
    Task<bool> NameExistsAsync(
        string name,
        CancellationToken cancellationToken = default);

    void Add(MaterialCategory category);
}
