using FluentValidation;

namespace Inventory.Application.Features.Inventory;

public sealed class MovementLineValidator : AbstractValidator<MovementLineInput>
{
    public MovementLineValidator(string? quantityMessage = null)
    {
        RuleFor(line => line.ProductId).NotEmpty();
        var quantity = RuleFor(line => line.Quantity).PrecisionScale(18, 4, ignoreTrailingZeros: true).GreaterThan(0);
        if (quantityMessage is not null) quantity.WithMessage(quantityMessage);
        RuleFor(line => line.UnitCost).PrecisionScale(18, 4, ignoreTrailingZeros: true).GreaterThanOrEqualTo(0);
    }
}

public sealed class MovementLinesValidator : AbstractValidator<IReadOnlyList<MovementLineInput>>
{
    public MovementLinesValidator(string? quantityMessage = null)
    {
        RuleFor(lines => lines).NotEmpty()
            .Must(lines => lines is null || lines.All(line => line is not null)
                && lines.Select(line => line.ProductId).Distinct().Count() == lines.Count)
            .WithMessage("A product may appear only once per movement.");
        RuleForEach(lines => lines).NotNull().SetValidator(new MovementLineValidator(quantityMessage));
    }
}

public abstract class MovementCommandValidator<T> : AbstractValidator<T> where T : IInventoryCommand
{
    protected MovementCommandValidator(string? quantityMessage = null)
    {
        RuleFor(command => command.IdempotencyKey).NotEmpty();
        RuleFor(command => command.Lines).NotNull().SetValidator(new MovementLinesValidator(quantityMessage));
    }
}
