using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Catalog;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Catalog;

namespace Inventory.Application.Features.Products;

public sealed record CreateProductCommand(
    string Sku,
    string Code,
    string Name,
    string Description,
    Guid CategoryId,
    Guid BrandId,
    Guid UnitOfMeasureId,
    decimal MinimumStock) : IRequest<ProductDto>;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.BrandId).NotEmpty();
        RuleFor(x => x.UnitOfMeasureId).NotEmpty();
        RuleFor(x => x.MinimumStock).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, ProductDto>
{
    private readonly IApplicationDbContext _db;

    public CreateProductCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<ProductDto> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var sku = request.Sku.Trim();
        if (await _db.Products.AnyAsync(p => p.Sku == sku, cancellationToken))
            throw new ConflictException($"A product with SKU '{sku}' already exists.");

        await EnsureReferencesExist(request, cancellationToken);

        var product = Product.Create(
            request.Sku, request.Code, request.Name, request.Description,
            request.CategoryId, request.BrandId, request.UnitOfMeasureId, request.MinimumStock);

        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);

        return product.ToDto();
    }

    private async Task EnsureReferencesExist(CreateProductCommand request, CancellationToken ct)
    {
        if (!await _db.Categories.AnyAsync(c => c.Id == request.CategoryId, ct))
            throw NotFoundException.For("Category", request.CategoryId);
        if (!await _db.Brands.AnyAsync(b => b.Id == request.BrandId, ct))
            throw NotFoundException.For("Brand", request.BrandId);
        if (!await _db.UnitsOfMeasure.AnyAsync(u => u.Id == request.UnitOfMeasureId, ct))
            throw NotFoundException.For("UnitOfMeasure", request.UnitOfMeasureId);
    }
}
