using System.Text;
using Inventory.Application.Common.Validation;
using Inventory.Infrastructure.Security;
using Inventory.Persistence.Seed;
using Npgsql;

namespace Inventory.API.Configuration;

public sealed record RuntimeSettings(string Postgres, JwtOptions Jwt, bool InitializeDatabase, AdminSeedSettings Admin)
{
    public static RuntimeSettings Load(IConfiguration config, IHostEnvironment environment)
    {
        var postgres = ValidatePostgres(config.GetConnectionString("Postgres"));
        var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        ValidateJwt(jwt);
        var initialize = config.GetValue<bool?>("Database:InitializeOnStartup")
            ?? (environment.IsDevelopment() || environment.IsEnvironment("Testing"));
        return new RuntimeSettings(postgres, jwt, initialize, ReadAdmin(config, environment));
    }

    private static string ValidatePostgres(string? connection)
    {
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(connection ?? "");
            if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Database)
                || string.IsNullOrWhiteSpace(settings.Username)) throw new ArgumentException();
            return settings.ConnectionString;
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Configure ConnectionStrings__Postgres with Host, Database and Username.");
        }
    }

    private static void ValidateJwt(JwtOptions jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt.Issuer) || string.IsNullOrWhiteSpace(jwt.Audience)
            || string.IsNullOrWhiteSpace(jwt.SigningKey)
            || Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32
            || jwt.SigningKey.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            || jwt.AccessTokenMinutes is < 1 or > 60 || jwt.RefreshTokenDays is < 1 or > 30)
            throw new InvalidOperationException("Configure Jwt issuer, audience, a private signing key of at least 32 bytes, access lifetime (1–60 minutes) and refresh lifetime (1–30 days).");
    }

    private static AdminSeedSettings ReadAdmin(IConfiguration config, IHostEnvironment environment)
    {
        var email = config["BootstrapAdmin:Email"];
        var password = config["BootstrapAdmin:Password"];
        if (email is null && password is null && (environment.IsDevelopment() || environment.IsEnvironment("Testing")))
            return new AdminSeedSettings(DatabaseSeeder.DefaultAdminEmail, DatabaseSeeder.DefaultAdminPassword);
        if (email is null && password is null) return new AdminSeedSettings();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || string.IsNullOrWhiteSpace(password)
            || password.Length < 12 || password.Length > IdentityInputLimits.PasswordLength
            || Encoding.UTF8.GetByteCount(password) > IdentityInputLimits.PasswordUtf8Bytes || (!environment.IsDevelopment() && password == DatabaseSeeder.DefaultAdminPassword))
            throw new InvalidOperationException("Configure BootstrapAdmin__Email and BootstrapAdmin__Password together; use a private password of at least 12 characters and at most 72 UTF-8 bytes.");
        return new AdminSeedSettings(email, password);
    }
}
