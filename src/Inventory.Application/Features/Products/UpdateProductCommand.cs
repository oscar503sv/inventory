using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Catalog;

namespace Inventory.Application.Features.Products;

public sealed record UpdateProductCommand(
    Guid Id,
    string Name,
    string Description,
    Guid CategoryId,
    Guid BrandId,
    Guid UnitOfMeasureId,
    decimal MinimumStock) : IRequest<ProductDto>;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.MinimumStock).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, ProductDto>
{
    private readonly IApplicationDbContext _db;

    public UpdateProductCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<ProductDto> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw NotFoundException.For("Product", request.Id);

        product.Update(
            request.Name, request.Description,
            request.CategoryId, request.BrandId, request.UnitOfMeasureId, request.MinimumStock);

        await _db.SaveChangesAsync(cancellationToken);
        return product.ToDto();
    }
}
