using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Domain.Entities;
using Kanstraction.OnSite.Domain.Errors;
using MediatR;

namespace Kanstraction.OnSite.Application.Materials.Commands.SaveMaterial;

public sealed class SaveMaterialCommandHandler(
    IMaterialRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SaveMaterialCommand, Result<int>>
{
    public async Task<Result<int>> Handle(
        SaveMaterialCommand request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
            return Result<int>.Failure("material.name_required", "A material name is required.");

        if (request.PricePerUnit < 0)
            return Result<int>.Failure("material.price_negative", "Price cannot be negative.");

        if (await repository.NameExistsAsync(
                name,
                request.MaterialId,
                cancellationToken))
        {
            return Result<int>.Failure(
                "material.duplicate_name",
                "A material with this name already exists.");
        }

        try
        {
            if (!request.MaterialId.HasValue)
            {
                var material = Material.Create(
                    name,
                    request.Unit,
                    request.PricePerUnit,
                    request.EffectiveSince,
                    request.IsActive,
                    request.MaterialCategoryId);
                repository.Add(material);
                await unitOfWork.CommitAsync(cancellationToken);
                return Result<int>.Success(material.Id);
            }

            var existing = await repository.GetAsync(
                request.MaterialId.Value,
                cancellationToken);
            if (existing == null)
                return Result<int>.Failure("material.not_found", "The material was not found.");

            var priceChanged = existing.PricePerUnit != request.PricePerUnit;
            existing.Update(
                name,
                request.Unit,
                request.PricePerUnit,
                request.EffectiveSince,
                request.IsActive,
                request.MaterialCategoryId);

            if (priceChanged)
            {
                await repository.UpdateActiveUsageDatesAsync(
                    existing.Id,
                    request.EffectiveSince.Date,
                    cancellationToken);
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return Result<int>.Success(existing.Id);
        }
        catch (DomainRuleViolationException ex)
        {
            return Result<int>.Failure(ex.Code, ex.Message);
        }
    }
}
