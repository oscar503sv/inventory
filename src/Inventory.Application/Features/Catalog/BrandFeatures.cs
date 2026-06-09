using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Catalog;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Catalog;

namespace Inventory.Application.Features.Catalog;

public sealed record CreateBrandCommand(string Name, string Description) : IRequest<BrandDto>;

public sealed class CreateBrandCommandValidator : AbstractValidator<CreateBrandCommand>
{
    public CreateBrandCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
}

public sealed class CreateBrandCommandHandler : IRequestHandler<CreateBrandCommand, BrandDto>
{
    private readonly IApplicationDbContext _db;
    public CreateBrandCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<BrandDto> Handle(CreateBrandCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await _db.Brands.AnyAsync(b => b.Name == name, cancellationToken))
            throw new ConflictException($"A brand named '{name}' already exists.");

        var brand = new Brand(request.Name, request.Description);
        _db.Brands.Add(brand);
        await _db.SaveChangesAsync(cancellationToken);
        return new BrandDto(brand.Id, brand.Name, brand.Description);
    }
}

public sealed record GetBrandsQuery : IRequest<IReadOnlyList<BrandDto>>;

public sealed class GetBrandsQueryHandler : IRequestHandler<GetBrandsQuery, IReadOnlyList<BrandDto>>
{
    private readonly IApplicationDbContext _db;
    public GetBrandsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<BrandDto>> Handle(GetBrandsQuery request, CancellationToken cancellationToken)
        => await _db.Brands.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new BrandDto(b.Id, b.Name, b.Description))
            .ToListAsync(cancellationToken);
}
