using Inventory.API.Configuration;
using Inventory.Persistence.Seed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Inventory.API.IntegrationTests;

public sealed class RuntimeSettingsTests
{
    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    public void test_environment_defaults_only_initialize_development_and_testing(string name, bool initialize)
    {
        var settings = Load(Defaults(), name);
        Assert.Equal(initialize, settings.InitializeDatabase);
        Assert.Equal(initialize ? DatabaseSeeder.DefaultAdminEmail : null, settings.Admin.Email);
    }

    [Theory]
    [InlineData("ConnectionStrings:Postgres", "")]
    [InlineData("ConnectionStrings:Postgres", "Host=localhost;Username=user")]
    [InlineData("ConnectionStrings:Postgres", "not-a-connection-string")]
    [InlineData("Jwt:SigningKey", "short")]
    [InlineData("Jwt:SigningKey", "CHANGE_ME_123456789012345678901234567890")]
    [InlineData("Jwt:Issuer", "")]
    [InlineData("Jwt:Audience", "")]
    [InlineData("Jwt:AccessTokenMinutes", "0")]
    [InlineData("Jwt:AccessTokenMinutes", "61")]
    [InlineData("Jwt:RefreshTokenDays", "31")]
    public void test_invalid_runtime_configuration_fails_before_startup(string key, string value)
    {
        var config = Defaults();
        config[key] = value;
        var exception = Assert.Throws<InvalidOperationException>(() => Load(config));
        Assert.DoesNotContain(config["Jwt:SigningKey"]!, exception.Message);
        Assert.DoesNotContain("private-password", exception.Message);
    }

    [Fact]
    public void test_explicit_database_initialization_and_private_bootstrap_credentials_are_respected()
    {
        var config = Defaults();
        config["Database:InitializeOnStartup"] = "true";
        config["BootstrapAdmin:Email"] = "owner@example.com";
        config["BootstrapAdmin:Password"] = "private-bootstrap-password";
        var settings = Load(config);
        Assert.True(settings.InitializeDatabase);
        Assert.Equal("owner@example.com", settings.Admin.Email);
        config["Database:InitializeOnStartup"] = "false";
        Assert.False(Load(config, "Testing").InitializeDatabase);
    }

    [Theory]
    [InlineData(null, "private-bootstrap-password")]
    [InlineData("owner@example.com", null)]
    [InlineData("owner@example.com", "short")]
    [InlineData("owner@example.com", "Admin#12345")]
    public void test_incomplete_or_weak_production_bootstrap_configuration_is_rejected(string? email, string? password)
    {
        var config = Defaults();
        config["BootstrapAdmin:Email"] = email;
        config["BootstrapAdmin:Password"] = password;
        Assert.Throws<InvalidOperationException>(() => Load(config));
    }

    private static Dictionary<string, string?> Defaults() => new()
    {
        ["ConnectionStrings:Postgres"] = "Host=localhost;Database=inventory;Username=inventory;Password=private-password",
        ["Jwt:Issuer"] = "InventoryErp",
        ["Jwt:Audience"] = "InventoryErp",
        ["Jwt:SigningKey"] = new string('x', 48),
        ["Jwt:AccessTokenMinutes"] = "15",
        ["Jwt:RefreshTokenDays"] = "7"
    };

    private static RuntimeSettings Load(Dictionary<string, string?> values, string environment = "Production")
        => RuntimeSettings.Load(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new TestEnvironment(environment));

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Inventory.API";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
