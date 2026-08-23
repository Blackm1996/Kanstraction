using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.OnSite.Infrastructure.Data;

public sealed class EfMaterialCategoryRepository(OnSiteDbContext db)
    : IMaterialCategoryRepository
{
    public Task<bool> NameExistsAsync(
        string name,
        CancellationToken cancellationToken = default)
        => db.MaterialCategories.AnyAsync(
            category => EF.Functions.Collate(category.Name, "NOCASE") == name,
            cancellationToken);

    public void Add(MaterialCategory category)
        => db.MaterialCategories.Add(category);
}
