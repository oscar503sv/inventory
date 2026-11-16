using FluentValidation;
using Inventory.Application.Common.Validation;

namespace Inventory.Application.Features.Users;

public interface IUserProfileInput
{
    string Email { get; }
    string UserName { get; }
    string FullName { get; }
}

public sealed class UserProfileValidator<T> : AbstractValidator<T> where T : IUserProfileInput
{
    public UserProfileValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(IdentityInputLimits.EmailLength);
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(IdentityInputLimits.UserNameLength);
        RuleFor(x => x.FullName).NotNull().MaximumLength(IdentityInputLimits.FullNameLength);
    }
}
