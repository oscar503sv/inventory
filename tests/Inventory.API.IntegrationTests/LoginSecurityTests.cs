using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Identity;
using Inventory.Infrastructure.Security;
using Inventory.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class LoginSecurityTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    private const string Password = "LoginSecurity#12345";

    [Theory]
    [InlineData(false, false, "incorrect", false)]
    [InlineData(false, false, "non-authenticating-placeholder", false)]
    [InlineData(true, false, "incorrect", false)]
    [InlineData(true, false, Password, false)]
    [InlineData(true, true, "incorrect", false)]
    [InlineData(true, true, Password, true)]
    public async Task Every_login_checks_exactly_one_hash_before_rejecting_missing_or_inactive_users(
        bool exists, bool active, string password, bool succeeds)
    {
        var observer = new ObservedPasswordHasher();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IPasswordHasher>(observer)));
        using var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@security.test";
        var realHash = observer.Hash(Password);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (exists)
        {
            var user = User.Create(email, "security", "Security Test", realHash);
            if (!active) user.Deactivate();
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        var tokensBefore = await db.RefreshTokens.CountAsync();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(succeeds ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, response.StatusCode);
        var verification = Assert.Single(observer.Verifications);
        Assert.Equal(password, verification.Password);
        Assert.Equal(exists ? realHash : observer.DummyHash, verification.Hash);
        // This checks the cryptographic work, without unstable wall-clock timing assertions.
        Assert.Equal(realHash[..7], observer.DummyHash[..7]);
        Assert.Equal("$2a$12$", observer.DummyHash[..7]);
        Assert.Equal(tokensBefore + (succeeds ? 1 : 0), await db.RefreshTokens.CountAsync());
        if (!succeeds)
        {
            var body = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
            Assert.Equal("Invalid credentials.", body!.Detail);
            Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        }
    }

    [Fact]
    public void Dummy_hash_is_valid_and_has_the_current_password_cost()
    {
        var hasher = new BcryptPasswordHasher();
        Assert.True(hasher.Verify("non-authenticating-placeholder", hasher.DummyHash));
        Assert.Equal(hasher.Hash(Password)[..7], hasher.DummyHash[..7]);
    }

    private sealed class ObservedPasswordHasher : IPasswordHasher
    {
        private readonly BcryptPasswordHasher _inner = new();
        public List<(string Password, string Hash)> Verifications { get; } = [];
        public string DummyHash => _inner.DummyHash;
        public string Hash(string password) => _inner.Hash(password);
        public bool Verify(string password, string passwordHash)
        {
            Verifications.Add((password, passwordHash));
            return _inner.Verify(password, passwordHash);
        }
    }
}
