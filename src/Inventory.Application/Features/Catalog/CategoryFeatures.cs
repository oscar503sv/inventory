using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Catalog;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Catalog;

namespace Inventory.Application.Features.Catalog;

public sealed record CreateCategoryCommand(string Name, string Description) : IRequest<CategoryDto>;

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
}

public sealed class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, CategoryDto>
{
    private readonly IApplicationDbContext _db;
    public CreateCategoryCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<CategoryDto> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await _db.Categories.AnyAsync(c => c.Name == name, cancellationToken))
            throw new ConflictException($"A category named '{name}' already exists.");

        var category = new Category(request.Name, request.Description);
        _db.Categories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);
        return new CategoryDto(category.Id, category.Name, category.Description);
    }
}

public sealed record GetCategoriesQuery : IRequest<IReadOnlyList<CategoryDto>>;

public sealed class GetCategoriesQueryHandler : IRequestHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    private readonly IApplicationDbContext _db;
    public GetCategoriesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<CategoryDto>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
        => await _db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description))
            .ToListAsync(cancellationToken);
}
