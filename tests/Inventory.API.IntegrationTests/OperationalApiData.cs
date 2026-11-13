using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Features.Inventory;
using Inventory.Domain.Catalog;
using Inventory.Domain.Identity;
using Inventory.Domain.Warehouses;
using Inventory.Persistence;
using Inventory.Persistence.Seed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Common;
using Shared.Contracts.Identity;
using Shared.Contracts.Inventory;

namespace Inventory.API.IntegrationTests;

internal sealed record InventoryScenario(Guid Product, Guid OtherProduct, Guid Source, Guid Destination);
internal sealed record TestUser(Guid Id, string Email);

internal sealed class OperationalApiData(WebApplicationFactory<Program> factory)
{
    public const string UserPassword = "Operational#12345";

    public async Task<InventoryScenario> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var category = new Category("C" + suffix, "");
        var brand = new Brand("B" + suffix, "");
        var unit = new UnitOfMeasure(suffix, "Each");
        var source = Warehouse.Create("S" + suffix, "Source", "");
        var destination = Warehouse.Create("D" + suffix, "Destination", "");
        var product = Product.Create("P" + suffix, "P", "Product", "", category.Id, brand.Id, unit.Id, 0);
        var other = Product.Create("O" + suffix, "O", "Other", "", category.Id, brand.Id, unit.Id, 0);
        db.AddRange(category, brand, unit, source, destination, product, other);
        await db.SaveChangesAsync();
        return new(product.Id, other.Id, source.Id, destination.Id);
    }

    public async Task<TestUser> UserAsync(string[] allows, Guid[] warehouses, bool global = false,
        string? role = null, string[]? denies = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = $"op-{Guid.NewGuid():N}@inventory.local";
        var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(UserPassword);
        var user = User.Create(email, email, "Operational test", hash, global);
        if (role is not null) user.AssignRole((await db.Roles.SingleAsync(r => r.Name == role)).Id);
        var permissions = await db.Permissions.ToDictionaryAsync(p => p.Code);
        foreach (var code in allows) user.SetOverride(permissions[code].Id, OverrideType.Allow);
        foreach (var code in denies ?? []) user.SetOverride(permissions[code].Id, OverrideType.Deny);
        foreach (var warehouse in warehouses) user.GrantWarehouseScope(warehouse);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new(user.Id, email);
    }

    public async Task<HttpClient> LoginAsync(TestUser? user = null)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = user?.Email ?? DatabaseSeeder.DefaultAdminEmail,
            password = user is null ? DatabaseSeeder.DefaultAdminPassword : UserPassword
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResultDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    public static async Task<MovementDto> MoveAsync(HttpClient client, InventoryScenario data,
        string operation, decimal quantity, Guid? warehouse = null, Guid? product = null)
    {
        var lines = new[] { new MovementLineInput(product ?? data.Product, quantity, 2) };
        var key = Guid.NewGuid();
        object command = operation switch
        {
            "entries" => new CreateEntryCommand(warehouse ?? data.Source, lines, IdempotencyKey: key),
            "exits" => new CreateExitCommand(warehouse ?? data.Source, lines, IdempotencyKey: key),
            "adjustments" => new CreateAdjustmentCommand(warehouse ?? data.Source, lines, "Physical count", key),
            "transfers" => new CreateTransferCommand(data.Source, data.Destination, lines, IdempotencyKey: key),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        using var response = await client.PostAsJsonAsync("/api/v1/inventory/" + operation, command);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MovementDto>())!;
    }

    public static async Task<PagedResult<T>> PageAsync<T>(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PagedResult<T>>())!;
    }

    public static async Task<ProblemDetails> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
            Assert.Equal((int)expected, problem.Status);
            return problem;
        }
    }
}
