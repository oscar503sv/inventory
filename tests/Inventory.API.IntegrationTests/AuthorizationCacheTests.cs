using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Inventory.Infrastructure.Authorization;
using Inventory.Persistence;
using Inventory.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class AuthorizationCacheTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Fact]
    public async Task Repeated_authorization_reads_execute_one_bounded_load()
    {
        var counter = new AuthorizationQueryCounter();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(counter))));
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.Users.Where(u => u.Email == DatabaseSeeder.DefaultAdminEmail).Select(u => u.Id).SingleAsync();
        var provider = scope.ServiceProvider.GetRequiredService<IUserAuthorizationProvider>();
        counter.Reset();
        var first = await provider.GetAuthorizationDataAsync(id);
        var second = await provider.GetAuthorizationDataAsync(id);
        var third = await provider.GetAuthorizationDataAsync(id);
        Assert.Equal(4, counter.Count);
        Assert.Same(first, second);
        Assert.Same(first, third);
    }
    [Fact]
    public async Task Http_policies_handlers_and_warehouse_checks_share_the_same_request_load()
    {
        var counter = new AuthorizationQueryCounter();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(counter))));
        using var client = await new OperationalApiData(factory).LoginAsync();
        counter.Reset();
        using var created = await client.PostAsJsonAsync("/api/v1/users", new
        {
            email = $"{Guid.NewGuid():N}@cache.test",
            userName = "cache",
            fullName = "Cache Test",
            password = "CacheTest#12345",
            roleIds = Array.Empty<Guid>(),
            hasAllWarehousesAccess = false,
            warehouseScopeIds = Array.Empty<Guid>()
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(4, counter.AuthorizationCount);
        counter.Reset();
        using var stock = await client.GetAsync("/api/v1/inventory/stock");
        Assert.Equal(HttpStatusCode.OK, stock.StatusCode);
        Assert.Equal(4, counter.AuthorizationCount);
    }

    [Fact]
    public async Task Concurrent_reads_are_shared_and_invalidation_and_new_scopes_reload()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.Users.Where(u => u.Email == DatabaseSeeder.DefaultAdminEmail).Select(u => u.Id).SingleAsync();
        var provider = scope.ServiceProvider.GetRequiredService<IUserAuthorizationProvider>();
        var data = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => provider.GetAuthorizationDataAsync(id)));
        Assert.All(data, item => Assert.Same(data[0], item));
        provider.Invalidate(id);
        Assert.NotSame(data[0], await provider.GetAuthorizationDataAsync(id));
        using var next = fixture.Factory.Services.CreateScope();
        Assert.NotSame(data[0], await next.ServiceProvider.GetRequiredService<IUserAuthorizationProvider>().GetAuthorizationDataAsync(id));
        var raw = scope.ServiceProvider.GetRequiredService<UserAuthorizationProvider>();
        Assert.True(PermissionEngine.ComputeEffectivePermissions(await raw.GetAuthorizationDataAsync(id))
            .SetEquals(PermissionEngine.ComputeEffectivePermissions(data[0])));
        Assert.Empty(PermissionEngine.ComputeEffectivePermissions(await provider.GetAuthorizationDataAsync(Guid.NewGuid())));
    }

}

public sealed class AuthorizationQueryCounter : DbCommandInterceptor
{
    private int _count;
    private int _authorizationCount;
    public int AuthorizationCount => Volatile.Read(ref _authorizationCount);
    public int Count => Volatile.Read(ref _count);
    public void Reset()
    {
        Interlocked.Exchange(ref _count, 0);
        Interlocked.Exchange(ref _authorizationCount, 0);
    }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        if (command.CommandText.Contains("-- Authorization.", StringComparison.Ordinal)) Interlocked.Increment(ref _authorizationCount);
        return ValueTask.FromResult(result);
    }
}
