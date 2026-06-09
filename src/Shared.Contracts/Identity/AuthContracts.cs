namespace Shared.Contracts.Identity;

public sealed record AuthResultDto(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    Guid UserId,
    string Email,
    IReadOnlyList<string> Roles);

public sealed record UserDto(
    Guid Id,
    string Email,
    string UserName,
    string FullName,
    bool IsActive,
    bool HasAllWarehousesAccess,
    IReadOnlyList<string> Roles);

public sealed record RoleDto(Guid Id, string Name, string Description, IReadOnlyList<string> Permissions);
