using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;

namespace Inventory.Persistence.Seed;

/// <summary>
/// Seeds the permission catalog, the default roles with their permissions, and an initial
/// administrator user. Idempotent: safe to run on every startup.
/// </summary>
public sealed class DatabaseSeeder
{
    public const string DefaultAdminEmail = "admin@inventory.local";
    public const string DefaultAdminPassword = "Admin#12345";

    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;

    public DatabaseSeeder(AppDbContext db, IPasswordHasher passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedPermissionsAsync(cancellationToken);
        await SeedRolesAsync(cancellationToken);
        await SeedAdminUserAsync(cancellationToken);
    }

    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions.Select(p => p.Code).ToListAsync(ct);
        var missing = Permissions.All.Where(p => !existing.Contains(p.Key));

        foreach (var (code, description) in missing)
            _db.Permissions.Add(new Permission(code, description));

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        var permissionsByCode = await _db.Permissions.ToDictionaryAsync(p => p.Code, ct);
        var existingRoles = await _db.Roles.Include(r => r.Permissions).ToListAsync(ct);

        foreach (var (roleName, permissionCodes) in RoleDefinitions.DefaultPermissions)
        {
            var role = existingRoles.FirstOrDefault(r => r.Name == roleName);
            if (role is null)
            {
                role = new Role(roleName, $"{roleName} default role");
                _db.Roles.Add(role);
            }

            foreach (var code in permissionCodes)
            {
                if (permissionsByCode.TryGetValue(code, out var permission))
                    role.GrantPermission(permission.Id);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedAdminUserAsync(CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(u => u.Email == DefaultAdminEmail, ct))
            return;

        var adminRole = await _db.Roles.FirstAsync(r => r.Name == RoleDefinitions.Administrator, ct);

        var admin = User.Create(
            email: DefaultAdminEmail,
            userName: "admin",
            fullName: "System Administrator",
            passwordHash: _passwordHasher.Hash(DefaultAdminPassword),
            hasAllWarehousesAccess: true);

        admin.AssignRole(adminRole.Id);
        _db.Users.Add(admin);

        await _db.SaveChangesAsync(ct);
    }
}
