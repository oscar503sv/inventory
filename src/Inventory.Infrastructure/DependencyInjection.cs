using Inventory.Application.Common.Abstractions;
using Inventory.Infrastructure.Authorization;
using Inventory.Infrastructure.Events;
using Inventory.Infrastructure.Security;
using Inventory.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;

namespace Inventory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();

        services.AddScoped<IDomainEventDispatcher, LoggingDomainEventDispatcher>();
        services.AddScoped<UserAuthorizationProvider>();
        services.AddScoped<IUserAuthorizationProvider, RequestAuthorizationProvider>();
        services.AddScoped<IPermissionChecker, PermissionChecker>();

        return services;
    }
}
