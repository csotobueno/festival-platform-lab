using Microsoft.Extensions.DependencyInjection;

namespace Festival.Modules.Notifications;

public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        // Module services will be registered here when the first use case is implemented.
        return services;
    }
}
