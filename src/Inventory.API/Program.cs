using System.Text;
using System.Threading.RateLimiting;
using Inventory.API.Authentication;
using Inventory.API.Middleware;
using Inventory.Application;
using Inventory.Application.Common.Abstractions;
using Inventory.Infrastructure;
using Inventory.Infrastructure.Security;
using Inventory.Persistence;
using Inventory.Persistence.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
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
        .WriteTo.Console());

    var config = builder.Configuration;

    // --- Application composition (Clean Architecture layers) ---
    builder.Services.AddPersistence(config.GetConnectionString("Postgres")!);
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(config);

    // --- HTTP / current user ---
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddProblemDetails();

    // --- AuthN: JWT bearer ---
    var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
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

    // --- Rate limiting (PRD: Security) ---
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 100, Window = TimeSpan.FromMinutes(1) }));
    });

    builder.Services.AddHealthChecks();

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
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health");
    app.MapPrometheusScrapingEndpoint(); // exposes /metrics

    await MigrateAndSeedAsync(app);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Inventory API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static async Task MigrateAndSeedAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        Log.Information("Database migrated and seeded");
    }
    catch (Exception ex)
    {
        // Allow the app to boot (e.g. Swagger) even if the database is not reachable yet.
        Log.Warning(ex, "Database migration/seed skipped — is PostgreSQL running?");
    }
}

public partial class Program;
