using System.Net;
using System.Text.Json;
using Inventory.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class ReadinessTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Fact]
    public async Task test_readiness_migrated_postgres_is_healthy_without_authentication()
    {
        using var client = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ready")).StatusCode);
        Assert.Equal("Healthy", await client.GetStringAsync("/health"));
    }

    [Fact]
    public async Task test_database_outage_leaves_liveness_healthy_and_readiness_returns_safe_503()
    {
        using var factory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", "Host=127.0.0.1;Port=1;Database=unavailable_test;Username=unavailable;Timeout=1;Pooling=false");
            builder.UseSetting("Database:InitializeOnStartup", "false");
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "readiness-outage");
        Assert.Equal("Healthy", await client.GetStringAsync("/health"));
        var problem = await OperationalApiData.ProblemAsync(await client.GetAsync("/ready"), HttpStatusCode.ServiceUnavailable);
        Assert.Equal("readiness-outage", problem.Extensions["correlationId"]?.ToString());
        Assert.DoesNotContain("127.0.0.1", problem.Detail);
        Assert.DoesNotContain("unavailable_test", problem.Detail);
        Assert.DoesNotContain("Npgsql", problem.Detail);
        Assert.Equal("Healthy", await client.GetStringAsync("/health"));
    }

    [Fact]
    public async Task test_development_swagger_separates_v1_and_paged_v2_contracts()
    {
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();
        using var v1 = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        using var v2 = JsonDocument.Parse(await client.GetStringAsync("/swagger/v2/swagger.json"));
        Assert.True(v1.RootElement.GetProperty("paths").TryGetProperty("/api/v1/audit-logs", out _));
        Assert.False(v1.RootElement.GetProperty("paths").TryGetProperty("/api/v2/inventory/stock", out _));
        Assert.True(v2.RootElement.GetProperty("paths").TryGetProperty("/api/v2/inventory/stock", out _));
        Assert.False(v2.RootElement.GetProperty("paths").TryGetProperty("/api/v1/inventory/stock", out _));
    }

    [Fact]
    public async Task test_pending_migrations_make_readiness_unhealthy_until_applied()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = db.GetService<IMigrator>();
        using var client = fixture.Factory.CreateClient();
        try
        {
            await migrator.MigrateAsync("20260609001034_InitialCreate");
            await OperationalApiData.ProblemAsync(await client.GetAsync("/ready"), HttpStatusCode.ServiceUnavailable);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        }
        finally
        {
            await migrator.MigrateAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ready")).StatusCode);
    }
}
