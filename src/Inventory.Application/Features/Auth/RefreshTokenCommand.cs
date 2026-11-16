using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Identity;

namespace Inventory.Application.Features.Auth;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResultDto>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IDateTimeProvider _clock;

    public RefreshTokenCommandHandler(
        IApplicationDbContext db,
        IJwtTokenGenerator jwt,
        IRefreshTokenService refreshTokens,
        IDateTimeProvider clock)
    {
        _db = db;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _clock = clock;
    }

    public async Task<AuthResultDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var incomingHash = _refreshTokens.Hash(request.RefreshToken);

        var stored = await _db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == incomingHash, cancellationToken);
        if (stored is null || !stored.IsActive(now))
            throw new ForbiddenAccessException("Invalid or expired refresh token.");

        var user = await _db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            throw new ForbiddenAccessException("User is no longer active.");

        // Rotate the refresh token (PRD: refresh token rotation).
        var (token, tokenHash) = _refreshTokens.Generate();
        var replacement = new RefreshToken(user.Id, tokenHash, now, _refreshTokens.GetRefreshTokenExpiry(now));

        var roleIds = user.Roles.Select(r => r.RoleId).ToList();
        var roleNames = await _db.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        var accessToken = _jwt.GenerateAccessToken(user.Id, user.Email, roleNames);
        if (!await _db.TryRotateRefreshTokenAsync(stored.Id, replacement, now, cancellationToken))
            throw new ForbiddenAccessException("Invalid or expired refresh token.");

        return new AuthResultDto(
            accessToken, token, _jwt.GetAccessTokenExpiry(now), user.Id, user.Email, roleNames);
    }
}
