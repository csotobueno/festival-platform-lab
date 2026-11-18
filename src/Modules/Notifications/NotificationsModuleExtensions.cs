using Festival.Modules.Notifications.Application;
using Festival.Modules.Notifications.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Festival.Modules.Notifications;

public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<NotificationDbContext>(options =>
            NotificationDbContext.Configure(options, connectionString));
        services.AddScoped<INotificationRepository, EfNotificationRepository>();
        services.AddScoped<INotificationDelivery, SimulatedNotificationDelivery>();
        services.AddScoped<RequestNotificationUseCase>();
        return services;
    }
}
