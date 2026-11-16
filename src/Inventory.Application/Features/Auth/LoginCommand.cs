using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Application.Common.Validation;
using Inventory.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Identity;

namespace Inventory.Application.Features.Auth;

public sealed record LoginCommand(string Email, string Password) : IRequest<AuthResultDto>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(IdentityInputLimits.EmailLength);
        RuleFor(x => x.Password).NotEmpty().SetValidator(new PasswordValidator());
    }
}

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IDateTimeProvider _clock;

    public LoginCommandHandler(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwt,
        IRefreshTokenService refreshTokens,
        IDateTimeProvider clock)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _clock = clock;
    }

    public async Task<AuthResultDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        var isPasswordValid = _passwordHasher.Verify(request.Password, user?.PasswordHash ?? _passwordHasher.DummyHash);
        if (user is null || !user.IsActive || !isPasswordValid)
            throw new UnauthorizedException("Invalid credentials.");

        var roleIds = user.Roles.Select(r => r.RoleId).ToList();
        var roleNames = await _db.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        var now = _clock.UtcNow;
        var accessToken = _jwt.GenerateAccessToken(user.Id, user.Email, roleNames);
        var (token, tokenHash) = _refreshTokens.Generate();

        _db.RefreshTokens.Add(new RefreshToken(user.Id, tokenHash, now, _refreshTokens.GetRefreshTokenExpiry(now)));
        await _db.SaveChangesAsync(cancellationToken);

        return new AuthResultDto(
            accessToken, token, _jwt.GetAccessTokenExpiry(now), user.Id, user.Email, roleNames);
    }
}
