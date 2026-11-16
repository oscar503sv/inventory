using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Features.Users;
using Inventory.Domain.Identity;
using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Common;
using Shared.Contracts.Identity;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class UserPagingTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Fact]
    public async Task Users_are_bounded_stable_and_include_only_the_requested_page_with_role_names()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().DummyHash;
        var prefix = Guid.NewGuid().ToString("N");
        var users = Enumerable.Range(0, 205).Select(i => User.Create($"paging-{prefix}-{i:D3}@test.local", "paging", "Paged User", hash)).ToArray();
        var roles = await db.Roles.OrderBy(r => r.Name).Take(2).ToArrayAsync();
        foreach (var role in roles) users[0].AssignRole(role.Id);
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        var ids = await db.Users.AsNoTracking().OrderBy(u => u.Email).ThenBy(u => u.Id).Select(u => u.Id).ToArrayAsync();
        using var client = await new OperationalApiData(fixture.Factory).LoginAsync();
        var first = (await client.GetFromJsonAsync<PagedResult<UserDto>>("/api/v2/users?page=1&pageSize=3"))!;
        var second = (await client.GetFromJsonAsync<PagedResult<UserDto>>("/api/v2/users?page=2&pageSize=3"))!;
        Assert.Equal(ids.Length, first.TotalCount);
        Assert.Equal((ids.Length + 2) / 3, first.TotalPages);
        Assert.Equal(1, first.Page);
        Assert.Equal(3, first.PageSize);
        Assert.Equal(ids.Take(3), first.Items.Select(u => u.Id));
        Assert.Equal(ids.Skip(3).Take(3), second.Items.Select(u => u.Id));
        var legacy = (await client.GetFromJsonAsync<UserDto[]>("/api/v1/users?page=2&pageSize=3"))!;
        Assert.Equal(second.Items.Select(u => u.Id), legacy.Select(u => u.Id));
        Assert.Equal(20, (await client.GetFromJsonAsync<UserDto[]>("/api/v1/users"))!.Length);
        var maximum = (await client.GetFromJsonAsync<PagedResult<UserDto>>("/api/v2/users?pageSize=200"))!;
        Assert.Equal(200, maximum.Items.Count);
        var index = Array.IndexOf(ids, users[0].Id);
        var assigned = (await client.GetFromJsonAsync<PagedResult<UserDto>>($"/api/v2/users?page={index + 1}&pageSize=1"))!;
        Assert.Equal(roles.Select(r => r.Name), Assert.Single(assigned.Items).Roles);
        var empty = (await client.GetFromJsonAsync<PagedResult<UserDto>>($"/api/v2/users?page={ids.Length + 1}&pageSize=1"))!;
        Assert.Empty(empty.Items);
        Assert.Equal(ids.Length, empty.TotalCount);
        var payload = await client.GetStringAsync("/api/v2/users");
        Assert.DoesNotContain("passwordHash", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Permissions_return_totals_and_preserve_legacy_array_contract()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expected = await db.Permissions.OrderBy(p => p.Code).ThenBy(p => p.Id).Select(p => p.Id).ToArrayAsync();
        using var client = await new OperationalApiData(fixture.Factory).LoginAsync();
        var page = (await client.GetFromJsonAsync<PagedResult<PermissionCatalogItem>>("/api/v2/permissions?page=2&pageSize=3"))!;
        Assert.Equal(expected.Length, page.TotalCount);
        Assert.Equal(expected.Skip(3).Take(3), page.Items.Select(p => p.Id));
        var legacy = (await client.GetFromJsonAsync<PermissionCatalogItem[]>("/api/v1/permissions?offset=3&limit=3"))!;
        Assert.Equal(page.Items.Select(p => p.Id), legacy.Select(p => p.Id));
        var empty = (await client.GetFromJsonAsync<PagedResult<PermissionCatalogItem>>("/api/v2/permissions?page=1000&pageSize=1"))!;
        Assert.Empty(empty.Items);
        Assert.Equal(expected.Length, empty.TotalCount);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=2147483647")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=201")]
    public async Task Paging_validation_rejects_invalid_or_overflowing_inputs(string query)
    {
        using var client = await new OperationalApiData(fixture.Factory).LoginAsync();
        foreach (var endpoint in new[] { "/api/v1/users", "/api/v2/users", "/api/v2/permissions" })
        {
            using var response = await client.GetAsync(endpoint + "?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("users")]
    [InlineData("permissions")]
    public async Task Paged_identity_endpoints_enforce_authentication_and_permissions(string endpoint)
    {
        var data = new OperationalApiData(fixture.Factory);
        var user = await data.UserAsync([], []);
        using var caller = await data.LoginAsync(user);
        using var anonymous = fixture.Factory.CreateClient();
        using var unauthenticated = await anonymous.GetAsync("/api/v2/" + endpoint);
        using var forbidden = await caller.GetAsync("/api/v2/" + endpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
