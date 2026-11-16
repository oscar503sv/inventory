using System.Reflection;
using FluentValidation;
using Inventory.Application.Common.Behaviors;
using Inventory.Application.Features.Users;
using Inventory.Application.Features.Inventory;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped<UserAccessAuthorization>();
        services.AddScoped<InventoryMovementWriter>();
        services.AddSingleton<IInventoryMovementStrategy, EntryStockStrategy>();
        services.AddSingleton<IInventoryMovementStrategy, ExitStockStrategy>();
        services.AddSingleton<IInventoryMovementStrategy, TransferStockStrategy>();
        services.AddSingleton<IInventoryMovementStrategy, AdjustmentStockStrategy>();

        return services;
    }
}
