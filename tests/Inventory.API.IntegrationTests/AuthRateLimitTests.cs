using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.API.Configuration;
using Inventory.API.RateLimiting;
using Inventory.Persistence.Seed;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Identity;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class AuthRateLimitTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    private WebApplicationFactory<Program> Factory(int ipLimit = 3, int accountLimit = 100, int seconds = 60,
        string? trustedNetwork = null, string? trustedProxy = null) => fixture.Factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("AuthRateLimit:IpPermitLimit", ipLimit.ToString());
        builder.UseSetting("AuthRateLimit:AccountPermitLimit", accountLimit.ToString());
        builder.UseSetting("AuthRateLimit:WindowSeconds", seconds.ToString());
        if (trustedProxy is not null) builder.UseSetting("ReverseProxy:KnownProxies:0", trustedProxy);
        if (trustedNetwork is not null) builder.UseSetting("ReverseProxy:KnownNetworks:0", trustedNetwork);
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestPeerFilter>());
    });

    private static HttpRequestMessage Login(string email, string ip, string password = "incorrect-password",
        string peer = "127.0.0.1")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        request.Headers.Add("X-Forwarded-For", ip);
        request.Headers.Add("X-Test-Peer", peer);
        return request;
    }

    private static async Task AssertLimited(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.TryGetProperty("correlationId", out _));
        }
    }

    [Fact]
    public async Task Ip_policy_combines_login_and_refresh_and_does_not_limit_health()
    {
        using var factory = Factory(ipLimit: 2);
        using var client = factory.CreateClient();
        using var first = await client.SendAsync(Login("missing-a@test.local", "192.0.2.1"));
        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        { Content = JsonContent.Create(new { refreshToken = "invalid" }) };
        refreshRequest.Headers.Add("X-Forwarded-For", "192.0.2.1");
        refreshRequest.Headers.Add("X-Test-Peer", "127.0.0.1");
        using var refresh = await client.SendAsync(refreshRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        await AssertLimited(await client.SendAsync(Login("missing-b@test.local", "192.0.2.1")));
        using var independent = await client.SendAsync(Login("missing-c@test.local", "192.0.2.2"));
        Assert.Equal(HttpStatusCode.Unauthorized, independent.StatusCode);
        Assert.Equal("Healthy", await client.GetStringAsync("/health"));
    }

    [Fact]
    public async Task Normalized_account_is_blocked_across_addresses_and_recovers_after_window()
    {
        using var factory = Factory(ipLimit: 100, accountLimit: 2, seconds: 5);
        using var client = factory.CreateClient();
        using var first = await client.SendAsync(Login(" Unknown@Test.Local ", "192.0.2.1"));
        using var second = await client.SendAsync(Login("unknown@test.local", "192.0.2.2"));
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        using var blocked = await client.SendAsync(Login("UNKNOWN@test.local", "192.0.2.3"));
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        var delay = blocked.Headers.RetryAfter!.Delta!.Value + TimeSpan.FromMilliseconds(250);
        await Task.Delay(delay);
        using var recovered = await client.SendAsync(Login("unknown@test.local", "192.0.2.4"));
        Assert.Equal(HttpStatusCode.Unauthorized, recovered.StatusCode);
    }

    [Fact]
    public async Task Refresh_uses_the_same_account_budget_as_login_from_other_addresses()
    {
        using var factory = Factory(ipLimit: 100, accountLimit: 1);
        using var client = factory.CreateClient();
        using var login = await client.SendAsync(Login(DatabaseSeeder.DefaultAdminEmail, "192.0.2.10",
            DatabaseSeeder.DefaultAdminPassword));
        login.EnsureSuccessStatusCode();
        var auth = (await login.Content.ReadFromJsonAsync<AuthResultDto>())!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        { Content = JsonContent.Create(new { auth.RefreshToken }) };
        request.Headers.Add("X-Forwarded-For", "192.0.2.11");
        request.Headers.Add("X-Test-Peer", "127.0.0.1");
        await AssertLimited(await client.SendAsync(request));
    }

    [Theory]
    [InlineData("127.0.0.1", null, false)]
    [InlineData("198.51.100.10", "198.51.100.0/24", false)]
    [InlineData("198.51.100.10", null, true)]
    [InlineData("198.51.100.10", null, false, "198.51.100.10")]
    public async Task Only_trusted_proxies_can_change_client_address(string peer, string? network, bool blocked, string? proxy = null)
    {
        using var factory = Factory(ipLimit: 1, trustedNetwork: network, trustedProxy: proxy);
        using var client = factory.CreateClient();
        using var first = await client.SendAsync(Login("one@test.local", "192.0.2.30", peer: peer));
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        using var next = await client.SendAsync(Login("two@test.local", "192.0.2.31", peer: peer));
        Assert.Equal(blocked ? HttpStatusCode.TooManyRequests : HttpStatusCode.Unauthorized, next.StatusCode);
    }

    [Fact]
    public async Task Ipv6_addresses_share_their_64_prefix_budget()
    {
        using var factory = Factory(ipLimit: 1);
        using var client = factory.CreateClient();
        using var first = await client.SendAsync(Login("one@test.local", "2001:db8:1:2::1"));
        await AssertLimited(await client.SendAsync(Login("two@test.local", "2001:db8:1:2::ffff")));
        using var separate = await client.SendAsync(Login("three@test.local", "2001:db8:1:3::1"));
        Assert.Equal(HttpStatusCode.Unauthorized, separate.StatusCode);
    }

    [Theory]
    [InlineData("::ffff:192.0.2.1", "192.0.2.1")]
    [InlineData("2001:db8:1:2:3:4:5:6", "2001:db8:1:2::/64")]
    public void Address_keys_are_canonical(string input, string expected)
        => Assert.Equal(expected, ClientAddress.PartitionKey(IPAddress.Parse(input)));

    [Theory]
    [InlineData("AuthRateLimit:IpPermitLimit", "0")]
    [InlineData("AuthRateLimit:AccountPermitLimit", "-1")]
    [InlineData("AuthRateLimit:WindowSeconds", "0")]
    [InlineData("ReverseProxy:ForwardLimit", "0")]
    [InlineData("ReverseProxy:KnownProxies:0", "invalid")]
    [InlineData("ReverseProxy:KnownNetworks:0", "0.0.0.0/0")]
    public void Invalid_configuration_is_rejected(string key, string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
        Assert.Throws<InvalidOperationException>(() =>
        {
            AuthRateLimitSettings.Load(config);
            TrustedProxySettings.Load(config);
        });
    }

    // TestServer has no socket peer. Set it explicitly before production forwarded-header handling.
    private sealed class TestPeerFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers["X-Test-Peer"].FirstOrDefault() ?? "127.0.0.1");
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
