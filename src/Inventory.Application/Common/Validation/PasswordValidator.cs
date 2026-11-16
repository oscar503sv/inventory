using System.Text;
using FluentValidation;

namespace Inventory.Application.Common.Validation;

public sealed class PasswordValidator : AbstractValidator<string>
{
    public PasswordValidator()
    {
        RuleFor(password => password).MaximumLength(IdentityInputLimits.PasswordLength)
            .Must(password => password is null || Encoding.UTF8.GetByteCount(password) <= IdentityInputLimits.PasswordUtf8Bytes)
            .WithMessage("Password must not exceed 72 UTF-8 bytes.");
    }
}
