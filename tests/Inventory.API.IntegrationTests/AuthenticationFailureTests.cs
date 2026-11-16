using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Identity;
using Inventory.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class AuthenticationFailureTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Theory]
    [InlineData("unknown")]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("inactive")]
    public async Task Invalid_refresh_returns_401_problem_with_bearer_challenge(string state)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = User.Create($"{Guid.NewGuid():N}@security.test", "security", "Test", passwords.DummyHash);
        if (state == "inactive") user.Deactivate();
        var (raw, hash) = tokens.Generate();
        var now = clock.UtcNow;
        var token = new RefreshToken(user.Id, hash, now.AddDays(-2), state == "expired" ? now.AddDays(-1) : now.AddDays(1));
        if (state == "revoked") token.Revoke(now);
        if (state != "unknown")
        {
            db.Users.Add(user);
            db.RefreshTokens.Add(token);
            await db.SaveChangesAsync();
        }
        using var client = fixture.Factory.CreateClient();
        var correlation = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlation);
        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = raw });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal(401, problem.Status);
        Assert.Equal("Invalid or expired refresh token.", problem.Detail);
        Assert.Equal(correlation, problem.Extensions["correlationId"]?.ToString());
        Assert.DoesNotContain(raw, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("login", "{}")]
    [InlineData("refresh", "{}")]
    public async Task Missing_credentials_remain_validation_errors(string endpoint, string body)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PostAsync($"/api/v1/auth/{endpoint}",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
