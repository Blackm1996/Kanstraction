using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Domain.Entities;
using MediatR;

namespace Kanstraction.OnSite.Application.Materials.Commands.CreateMaterialCategory;

public sealed class CreateMaterialCategoryCommandHandler(
    IMaterialCategoryRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateMaterialCategoryCommand, Result<int>>
{
    public async Task<Result<int>> Handle(
        CreateMaterialCategoryCommand request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            return Result<int>.Failure(
                "material_category.name_required",
                "A category name is required.");
        }

        if (await repository.NameExistsAsync(name, cancellationToken))
        {
            return Result<int>.Failure(
                "material_category.duplicate_name",
                "A category with this name already exists.");
        }

        var category = MaterialCategory.Create(name);
        repository.Add(category);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<int>.Success(category.Id);
    }
}
