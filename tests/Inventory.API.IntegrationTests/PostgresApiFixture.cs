using Inventory.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Cryptography;

namespace Inventory.API.IntegrationTests;

public sealed class PostgresApiFixture : IAsyncLifetime
{
    private readonly string _database = $"inventario_erp_test_{Guid.NewGuid():N}";
    private string? _adminConnection;
    private bool _created;
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public ConcurrentSaveGate SaveGate { get; } = new();

    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Defina TEST_POSTGRES_CONNECTION con un rol PostgreSQL con CREATEDB. Las pruebas nunca se omiten por falta de PostgreSQL.");
        var settings = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres", Pooling = false };
        _adminConnection = settings.ConnectionString;
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin);
        await create.ExecuteNonQueryAsync();
        _created = true;
        settings.Database = _database;
        Factory = new ApiFactory(settings.ConnectionString, SaveGate);
        try
        {
            // Start the real API: migrations, seed, authentication and interceptors remain enabled.
            using var client = Factory.CreateClient();
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(_database, db.Database.GetDbConnection().Database);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        if (!_created) return;
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{_database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
        _created = false;
    }

    private sealed class ApiFactory(string connection, ConcurrentSaveGate gate) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Postgres", connection);
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            builder.ConfigureServices(services =>
            {
                services.AddDbContext<AppDbContext>(options => options.AddInterceptors(gate));
                // Inventory regression suites deliberately exceed the public per-minute quota.
                services.Configure<RateLimiterOptions>(options => options.GlobalLimiter = null);
            });
        }
    }
}
