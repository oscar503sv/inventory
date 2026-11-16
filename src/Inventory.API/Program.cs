using System.Text;
using System.Threading.RateLimiting;
using Inventory.API.Authentication;
using Inventory.API.Configuration;
using Inventory.API.Health;
using Inventory.API.Middleware;
using Inventory.API.RateLimiting;
using Inventory.Application;
using Inventory.Application.Common.Abstractions;
using Inventory.Infrastructure;
using Inventory.Persistence;
using Inventory.Persistence.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Json;
using Shared.Authorization;
using Shared.Authorization.AspNetCore;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new JsonFormatter()));

    var config = builder.Configuration;
    var settings = RuntimeSettings.Load(config, builder.Environment);

    // --- Application composition (Clean Architecture layers) ---
    builder.Services.AddPersistence(settings.Postgres);
    builder.Services.AddSingleton(settings.Admin);
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(config);

    // --- HTTP / current user ---
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddProblemDetails();

    // --- AuthN: JWT bearer ---
    var jwt = settings.Jwt;
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var id = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    var db = context.HttpContext.RequestServices.GetRequiredService<IApplicationDbContext>();
                    if (!Guid.TryParse(id, out var userId) ||
                        !await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, context.HttpContext.RequestAborted))
                        context.Fail("User is no longer active.");
                }
            };
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

    // --- AuthZ: permission policies (ADR-003) ---
    builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
    builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
    builder.Services.AddAuthorization();

    var proxySettings = TrustedProxySettings.Load(config);
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = proxySettings.ForwardedHeaders;
        options.ForwardLimit = proxySettings.ForwardLimit;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in proxySettings.KnownProxies) options.KnownProxies.Add(proxy);
        foreach (var network in proxySettings.KnownIPNetworks) options.KnownIPNetworks.Add(network);
    });
    var authRateSettings = AuthRateLimitSettings.Load(config);
    builder.Services.AddSingleton(authRateSettings);
    builder.Services.AddSingleton<AuthRateLimits>();
    builder.Services.AddScoped<AuthAccountRateLimitFilter>();

    // --- Rate limiting (PRD: Security) ---
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, _) => new ValueTask(AuthRateLimits.RejectAsync(context.HttpContext, context.Lease));
        options.AddPolicy(AuthRateLimits.PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
            ClientAddress.PartitionKey(context.Connection.RemoteIpAddress),
            _ => AuthRateLimits.Window(authRateSettings.IpPermitLimit, authRateSettings.WindowSeconds)));
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                ClientAddress.PartitionKey(httpContext.Connection.RemoteIpAddress),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 100, Window = TimeSpan.FromMinutes(1) }));
    });

    builder.Services.AddHealthChecks().AddCheck<PostgresReadinessCheck>("postgres",
        tags: ["ready"], timeout: TimeSpan.FromSeconds(3));

    // --- Observability: OpenTelemetry tracing + Prometheus metrics (PRD: Observability) ---
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("Inventory.API"))
        .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddPrometheusExporter());

    // --- Swagger with JWT support ---
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "Inventory ERP API", Version = "v1" });
        options.SwaggerDoc("v2", new OpenApiInfo { Title = "Inventory ERP paged queries", Version = "v2" });
        options.DocInclusionPredicate((document, api) => (api.GroupName ?? "v1") == document);
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header
        });
        options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer")] = new List<string>()
        });
    });

    var app = builder.Build();

    // --- Pipeline ---
    app.UseForwardedHeaders();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseStatusCodePages(ProblemResponses.WriteStatusAsync);
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Inventory ERP v1");
            options.SwaggerEndpoint("/swagger/v2/swagger.json", "Paged inventory queries v2");
        });
    }

    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = ReadinessResponse.WriteAsync
    }).AllowAnonymous();
    app.MapPrometheusScrapingEndpoint(); // exposes /metrics

    if (settings.InitializeDatabase || args.Contains("--migrate-only"))
        await MigrateAndSeedAsync(app);

    if (!args.Contains("--migrate-only"))
        app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Inventory API terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

static async Task MigrateAndSeedAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    Log.Information("Database migrated and seeded");
}

public partial class Program;
