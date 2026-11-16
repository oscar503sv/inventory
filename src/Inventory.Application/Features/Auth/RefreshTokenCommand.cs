using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Audit;
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
    private readonly ICurrentUser _currentUser;

    public RefreshTokenCommandHandler(
        IApplicationDbContext db,
        IJwtTokenGenerator jwt,
        IRefreshTokenService refreshTokens,
        IDateTimeProvider clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<AuthResultDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var incomingHash = _refreshTokens.Hash(request.RefreshToken);

        var stored = await _db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == incomingHash, cancellationToken);
        if (stored is not null) await RejectReuseAsync(stored, now, cancellationToken);
        if (stored is null || !stored.IsActive(now))
            throw new UnauthorizedException("Invalid or expired refresh token.");

        var user = await _db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            throw new UnauthorizedException("Invalid or expired refresh token.");

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
        {
            var current = await _db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == stored.Id, cancellationToken);
            await RejectReuseAsync(current, now, cancellationToken);
            throw new UnauthorizedException("Invalid or expired refresh token.");
        }

        return new AuthResultDto(
            accessToken, token, _jwt.GetAccessTokenExpiry(now), user.Id, user.Email, roleNames);
    }

    private async Task RejectReuseAsync(RefreshToken stored, DateTime now, CancellationToken ct)
    {
        if (stored.ReplacedByTokenHash is null) return;

        await _db.ExecuteSessionUpdateAsync(stored.UserId, async () =>
        {
            await SessionRevocation.RevokeAsync(_db, stored.UserId, now, ct);
            _db.AuditLogs.Add(new AuditLog(stored.UserId, now, "Session", "RefreshTokenReuseDetected",
                null, null, _currentUser.IpAddress, _currentUser.CorrelationId));
            await _db.SaveChangesAsync(ct);
        }, ct);
        // Throw after commit so the rejection cannot roll back revocation or its audit record.
        throw new UnauthorizedException("Invalid or expired refresh token.");
    }
}
