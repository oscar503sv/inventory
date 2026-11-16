using System.Linq.Expressions;
using FluentValidation;

namespace Inventory.Application.Common.Validation;

public sealed class WarehouseScopeValidator<T> : AbstractValidator<T>
{
    public WarehouseScopeValidator(Expression<Func<T, IReadOnlyList<Guid>>> scopes, Func<T, bool> hasGlobalAccess)
    {
        RuleFor(scopes).NotNull();
        RuleFor(scopes).Must((input, warehouses) => !hasGlobalAccess(input) || warehouses is not { Count: > 0 })
            .WithMessage("Global access cannot include individual warehouse scopes.");
    }
}
