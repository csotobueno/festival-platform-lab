using Festival.Modules.Notifications.Application;

namespace Festival.Modules.Notifications.Infrastructure;

internal sealed class SimulatedNotificationDelivery : INotificationDelivery
{
    public Task<NotificationDeliveryResult> DeliverAsync(string recipientEmail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(NotificationDeliveryResult.Sent);
    }
}
