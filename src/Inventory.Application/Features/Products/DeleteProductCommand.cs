using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Features.Products;

/// <summary>
/// Deactivates a product (soft delete). Products are never hard-deleted because historical inventory
/// movements reference them (ADR-001 traceability).
/// </summary>
public sealed record DeleteProductCommand(Guid Id) : IRequest;

public sealed class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand>
{
    private readonly IApplicationDbContext _db;

    public DeleteProductCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw NotFoundException.For("Product", request.Id);

        product.Deactivate();
        await _db.SaveChangesAsync(cancellationToken);
    }
}
