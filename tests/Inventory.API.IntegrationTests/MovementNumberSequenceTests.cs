using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Features.Inventory;
using Inventory.Domain.Inventory;
using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Inventory;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class MovementNumberSequenceTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Fact]
    public async Task Concurrent_instances_allocate_unique_numbers_that_fit_the_existing_column()
    {
        var tasks = Enumerable.Range(0, 40).Select(async index =>
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var generator = scope.ServiceProvider.GetRequiredService<IMovementNumberGenerator>();
            return await generator.NextAsync((MovementType)(index % 4 + 1));
        });
        var numbers = await Task.WhenAll(tasks);
        Assert.Equal(numbers.Length, numbers.Distinct().Count());
        Assert.Equal(numbers.Length, numbers.Select(number => number[4..]).Distinct().Count());
        Assert.All(numbers, number => Assert.Matches("^(ENT|EXI|TRF|ADJ)-[0-9]{19}$", number));
    }

    [Fact]
    public async Task Upgrade_after_rollback_preserves_issued_numbers_and_replay_does_not_allocate_a_new_number()
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        var command = new CreateEntryCommand(data.Source, [new(data.Product, 1)], IdempotencyKey: Guid.NewGuid());
        using var response = await client.PostAsJsonAsync("/api/v1/inventory/entries", command);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var first = (await response.Content.ReadFromJsonAsync<MovementDto>())!;
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261116052329_InventoryRelationalConstraints");
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var before = await db.Database.SqlQuery<long>($"SELECT last_value AS \"Value\" FROM inventory_movement_number_seq").SingleAsync();
        using var replay = await client.PostAsJsonAsync("/api/v1/inventory/entries", command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(first.MovementNumber, (await replay.Content.ReadFromJsonAsync<MovementDto>())!.MovementNumber);
        Assert.Equal(before, await db.Database.SqlQuery<long>($"SELECT last_value AS \"Value\" FROM inventory_movement_number_seq").SingleAsync());
        var next = await OperationalApiData.MoveAsync(client, data, "entries", 1);
        Assert.True(long.Parse(next.MovementNumber[4..]) > long.Parse(first.MovementNumber[4..]));
        Assert.Equal(first.MovementNumber, await db.InventoryMovements.Where(m => m.Id == first.Id).Select(m => m.MovementNumber).SingleAsync());
    }
}
