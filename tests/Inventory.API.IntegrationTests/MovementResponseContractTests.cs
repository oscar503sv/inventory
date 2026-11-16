using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Application.Features.Inventory;
using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;
using Shared.Contracts.Inventory;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class MovementResponseContractTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Theory]
    [InlineData("entries")]
    [InlineData("exits")]
    [InlineData("transfers")]
    [InlineData("adjustments")]
    public async Task Creation_is_201_with_resolvable_location_and_replay_is_200_with_the_same_dto(string operation)
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        await OperationalApiData.MoveAsync(client, data, "entries", 10);
        var command = Command(operation, data);
        using var first = await client.PostAsJsonAsync("/api/v1/inventory/" + operation, command);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var movement = (await first.Content.ReadFromJsonAsync<MovementDto>())!;
        var location = first.Headers.Location;
        Assert.NotNull(location);
        Assert.Equal($"/api/v1/inventory/movements/{movement.Id}", location.AbsolutePath);
        using var detail = await client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(movement.Id, (await detail.Content.ReadFromJsonAsync<MovementDto>())!.Id);
        using var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("id", out _));
        Assert.False(json.RootElement.TryGetProperty("isReplay", out _));
        Assert.False(json.RootElement.TryGetProperty("movement", out _));
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.InventoryMovements.CountAsync();
        var audit = await db.AuditLogs.CountAsync();
        using var replay = await client.PostAsJsonAsync("/api/v1/inventory/" + operation, command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Null(replay.Headers.Location);
        var repeated = (await replay.Content.ReadFromJsonAsync<MovementDto>())!;
        Assert.Equal(movement with { Lines = repeated.Lines }, repeated);
        Assert.Equal(movement.Lines, repeated.Lines);
        Assert.Equal(count, await db.InventoryMovements.CountAsync());
        Assert.Equal(audit, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Location_does_not_grant_read_permission_to_a_write_only_user()
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        var user = await helper.UserAsync([Permissions.Inventory.EntryCreate], [data.Source]);
        using var client = await helper.LoginAsync(user);
        using var created = await client.PostAsJsonAsync("/api/v1/inventory/entries", Command("entries", data));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        using var detail = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
    }

    internal static object Command(string operation, InventoryScenario data)
    {
        MovementLineInput[] lines = [new(data.Product, 2, 0)];
        return operation switch
        {
            "entries" => new CreateEntryCommand(data.Source, lines, "Contract", Guid.NewGuid()),
            "exits" => new CreateExitCommand(data.Source, lines, "Contract", Guid.NewGuid()),
            "transfers" => new CreateTransferCommand(data.Source, data.Destination, lines, "Contract", Guid.NewGuid()),
            "adjustments" => new CreateAdjustmentCommand(data.Source, lines, "Count", Guid.NewGuid()),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }
}
