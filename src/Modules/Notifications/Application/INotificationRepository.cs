using Festival.Modules.Notifications.Domain;

namespace Festival.Modules.Notifications.Application;

internal interface INotificationRepository
{
    // Each operation commits before completing. Add keeps the aggregate tracked for the final save.
    Task AddAsync(Notification notification, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
