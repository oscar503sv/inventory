namespace Inventory.API.IntegrationTests;

// Program uses Serilog's process-wide bootstrap logger. Hosts must start sequentially;
// concurrent HTTP requests inside each test still run against separate scopes/connections.
[CollectionDefinition("Postgres API", DisableParallelization = true)]
public sealed class PostgresApiCollection;
