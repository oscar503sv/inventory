using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Common.Abstractions;
using Inventory.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class MovementAuthenticationTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Theory]
    [InlineData("missing-id")]
    [InlineData("empty-id")]
    [InlineData("unauthenticated")]
    public async Task Writer_rejects_invalid_current_user_with_401_before_stock_or_movement_writes(string state)
    {
        var data = await new OperationalApiData(fixture.Factory).SeedAsync();
        var user = new InvalidCurrentUser(state);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<ICurrentUser>(user)));
        // The bearer principal is valid and passes endpoint authorization; this exercises the writer's own guard.
        using var client = await new OperationalApiData(factory).LoginAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var movements = await db.InventoryMovements.CountAsync();
        var stock = await db.Stocks.CountAsync();
        var audit = await db.AuditLogs.CountAsync();
        foreach (var operation in new[] { "entries", "exits", "transfers", "adjustments" })
        {
            using var response = await client.PostAsJsonAsync("/api/v1/inventory/" + operation,
                MovementResponseContractTests.Command(operation, data));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
            Assert.Null(response.Headers.Location);
        }
        Assert.Equal(movements, await db.InventoryMovements.CountAsync());
        Assert.Equal(stock, await db.Stocks.CountAsync());
        Assert.Equal(audit, await db.AuditLogs.CountAsync());
    }

    private sealed class InvalidCurrentUser(string state) : ICurrentUser
    {
        private readonly Guid _id = Guid.NewGuid();
        public Guid? UserId => state == "missing-id" ? null : state == "empty-id" ? Guid.Empty : _id;
        public bool IsAuthenticated => state != "unauthenticated";
        public string? Email => null;
        public string? IpAddress => null;
        public string? CorrelationId => null;
    }
}
