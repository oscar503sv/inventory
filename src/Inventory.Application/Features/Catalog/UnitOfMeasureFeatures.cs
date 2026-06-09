using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Catalog;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Catalog;

namespace Inventory.Application.Features.Catalog;

public sealed record CreateUnitOfMeasureCommand(string Code, string Name) : IRequest<UnitOfMeasureDto>;

public sealed class CreateUnitOfMeasureCommandValidator : AbstractValidator<CreateUnitOfMeasureCommand>
{
    public CreateUnitOfMeasureCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(16);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
    }
}

public sealed class CreateUnitOfMeasureCommandHandler : IRequestHandler<CreateUnitOfMeasureCommand, UnitOfMeasureDto>
{
    private readonly IApplicationDbContext _db;
    public CreateUnitOfMeasureCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<UnitOfMeasureDto> Handle(CreateUnitOfMeasureCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.UnitsOfMeasure.AnyAsync(u => u.Code == code, cancellationToken))
            throw new ConflictException($"A unit of measure with code '{code}' already exists.");

        var unit = new UnitOfMeasure(request.Code, request.Name);
        _db.UnitsOfMeasure.Add(unit);
        await _db.SaveChangesAsync(cancellationToken);
        return new UnitOfMeasureDto(unit.Id, unit.Code, unit.Name);
    }
}

public sealed record GetUnitsOfMeasureQuery : IRequest<IReadOnlyList<UnitOfMeasureDto>>;

public sealed class GetUnitsOfMeasureQueryHandler : IRequestHandler<GetUnitsOfMeasureQuery, IReadOnlyList<UnitOfMeasureDto>>
{
    private readonly IApplicationDbContext _db;
    public GetUnitsOfMeasureQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<UnitOfMeasureDto>> Handle(GetUnitsOfMeasureQuery request, CancellationToken cancellationToken)
        => await _db.UnitsOfMeasure.AsNoTracking()
            .OrderBy(u => u.Code)
            .Select(u => new UnitOfMeasureDto(u.Id, u.Code, u.Name))
            .ToListAsync(cancellationToken);
}
