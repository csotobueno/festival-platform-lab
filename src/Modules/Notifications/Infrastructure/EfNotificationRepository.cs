using Festival.Modules.Notifications.Application;
using Festival.Modules.Notifications.Domain;

namespace Festival.Modules.Notifications.Infrastructure;

internal sealed class EfNotificationRepository(NotificationDbContext context) : INotificationRepository
{
    public async Task AddAsync(Notification notification, CancellationToken cancellationToken)
    {
        context.Notifications.Add(notification);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
