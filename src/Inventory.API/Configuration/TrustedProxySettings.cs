using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Inventory.API.Configuration;

public static class TrustedProxySettings
{
    public static ForwardedHeadersOptions Load(IConfiguration config)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = config.GetValue<int?>("ReverseProxy:ForwardLimit") ?? 1
        };
        if (options.ForwardLimit is < 1 or > 10)
            throw new InvalidOperationException("ReverseProxy ForwardLimit must be between 1 and 10.");
        // Keep a trust entry even when no proxies are configured; empty lists mean trust everyone.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Add(IPAddress.IPv6Loopback);
        options.KnownProxies.Add(IPAddress.Loopback);
        foreach (var value in config.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        {
            if (!IPAddress.TryParse(value, out var address))
                throw new InvalidOperationException("ReverseProxy KnownProxies must contain valid IP addresses.");
            options.KnownProxies.Add(address);
        }
        foreach (var value in config.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
        {
            if (!System.Net.IPNetwork.TryParse(value, out var network) || network.PrefixLength == 0)
                throw new InvalidOperationException("ReverseProxy KnownNetworks must contain restricted CIDR networks.");
            options.KnownIPNetworks.Add(network);
        }
        return options;
    }
}
