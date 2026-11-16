using System.Linq.Expressions;
using FluentValidation;
using Inventory.Domain.Inventory;

namespace Inventory.Application.Features.Inventory;

public sealed class MovementLineValidator : AbstractValidator<MovementLineInput>
{
    public MovementLineValidator(string? quantityMessage = null)
    {
        RuleFor(line => line.ProductId).NotEmpty();
        var quantity = RuleFor(line => line.Quantity).PrecisionScale(InventoryNumericLimits.Precision, InventoryNumericLimits.Scale, ignoreTrailingZeros: true).GreaterThan(0);
        if (quantityMessage is not null) quantity.WithMessage(quantityMessage);
        RuleFor(line => line.UnitCost).PrecisionScale(InventoryNumericLimits.Precision, InventoryNumericLimits.Scale, ignoreTrailingZeros: true).GreaterThanOrEqualTo(0);
    }
}

public sealed class MovementLinesValidator : AbstractValidator<IReadOnlyList<MovementLineInput>>
{
    public MovementLinesValidator(string? quantityMessage = null)
    {
        RuleFor(lines => lines).Cascade(CascadeMode.Stop).NotEmpty()
            .Must(lines => lines is null || lines.Count <= MovementInputLimits.MaxLinesPerMovement)
            .WithMessage("A movement cannot contain more than 500 lines.")
            .Must(lines => lines is null || lines.All(line => line is not null)
                && lines.Select(line => line.ProductId).Distinct().Count() == lines.Count)
            .WithMessage("A product may appear only once per movement.");
        RuleForEach(lines => lines).NotNull().SetValidator(new MovementLineValidator(quantityMessage))
            .When(lines => lines is not null && lines.Count <= MovementInputLimits.MaxLinesPerMovement);
    }
}

public abstract class MovementCommandValidator<T> : AbstractValidator<T> where T : IInventoryCommand
{
    protected MovementCommandValidator(Expression<Func<T, string?>> notes, string? quantityMessage = null)
    {
        RuleFor(command => command.IdempotencyKey).NotEmpty();
        RuleFor(notes).MaximumLength(InventoryNumericLimits.MaxNotesLength);
        RuleFor(command => command.Lines).NotNull().SetValidator(new MovementLinesValidator(quantityMessage));
    }
}
