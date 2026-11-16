using System.Globalization;

namespace Inventory.API.Controllers;

public static class LegacyInventoryDeprecation
{
    public static readonly DateTimeOffset DeprecatedAt = new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset SunsetAt = new(2027, 5, 16, 0, 0, 0, TimeSpan.Zero);

    public static void Apply(HttpContext context, string successor)
    {
        context.Response.Headers["Deprecation"] = "@" + DeprecatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["Sunset"] = SunsetAt.ToString("R", CultureInfo.InvariantCulture);
        context.Response.Headers.Append("Link", $"<{context.Request.PathBase}{successor}>; rel=\"successor-version\"");
    }
}
