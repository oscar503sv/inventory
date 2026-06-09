using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Warehouses;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Warehouses;

namespace Inventory.Application.Features.Warehouses;

internal static class WarehouseMapping
{
    public static WarehouseDto ToDto(this Warehouse w) =>
        new(w.Id, w.Code, w.Name, w.Address, w.Status.ToString());
}

public sealed record CreateWarehouseCommand(string Code, string Name, string Address) : IRequest<WarehouseDto>;

public sealed class CreateWarehouseCommandValidator : AbstractValidator<CreateWarehouseCommand>
{
    public CreateWarehouseCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
    }
}

public sealed class CreateWarehouseCommandHandler : IRequestHandler<CreateWarehouseCommand, WarehouseDto>
{
    private readonly IApplicationDbContext _db;
    public CreateWarehouseCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<WarehouseDto> Handle(CreateWarehouseCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Warehouses.AnyAsync(w => w.Code == code, cancellationToken))
            throw new ConflictException($"A warehouse with code '{code}' already exists.");

        var warehouse = Warehouse.Create(request.Code, request.Name, request.Address);
        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync(cancellationToken);
        return warehouse.ToDto();
    }
}

public sealed record UpdateWarehouseCommand(Guid Id, string Name, string Address) : IRequest<WarehouseDto>;

public sealed class UpdateWarehouseCommandValidator : AbstractValidator<UpdateWarehouseCommand>
{
    public UpdateWarehouseCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
    }
}

public sealed class UpdateWarehouseCommandHandler : IRequestHandler<UpdateWarehouseCommand, WarehouseDto>
{
    private readonly IApplicationDbContext _db;
    public UpdateWarehouseCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<WarehouseDto> Handle(UpdateWarehouseCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken)
            ?? throw NotFoundException.For("Warehouse", request.Id);

        warehouse.Update(request.Name, request.Address);
        await _db.SaveChangesAsync(cancellationToken);
        return warehouse.ToDto();
    }
}

public sealed record GetWarehousesQuery : IRequest<IReadOnlyList<WarehouseDto>>;

public sealed class GetWarehousesQueryHandler : IRequestHandler<GetWarehousesQuery, IReadOnlyList<WarehouseDto>>
{
    private readonly IApplicationDbContext _db;
    public GetWarehousesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<WarehouseDto>> Handle(GetWarehousesQuery request, CancellationToken cancellationToken)
        => await _db.Warehouses.AsNoTracking()
            .OrderBy(w => w.Code)
            .Select(w => new WarehouseDto(w.Id, w.Code, w.Name, w.Address, w.Status.ToString()))
            .ToListAsync(cancellationToken);
}
