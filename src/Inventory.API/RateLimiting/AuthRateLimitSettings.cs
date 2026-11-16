namespace Inventory.API.RateLimiting;

public sealed class AuthRateLimitSettings
{
    public int IpPermitLimit { get; set; } = 10;
    public int AccountPermitLimit { get; set; } = 5;
    public int WindowSeconds { get; set; } = 60;

    public static AuthRateLimitSettings Load(IConfiguration config)
    {
        var settings = config.GetSection("AuthRateLimit").Get<AuthRateLimitSettings>() ?? new();
        if (settings.IpPermitLimit is < 1 or > 100000 || settings.AccountPermitLimit is < 1 or > 100000
            || settings.WindowSeconds is < 1 or > 3600)
            throw new InvalidOperationException("Configure positive AuthRateLimit permit limits (up to 100000) and WindowSeconds (1–3600).");
        return settings;
    }
}
