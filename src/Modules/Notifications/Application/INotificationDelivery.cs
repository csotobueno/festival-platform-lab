namespace Festival.Modules.Notifications.Application;

internal interface INotificationDelivery
{
    // Known provider rejection/timeout is Failed; unexpected exceptions and cancellation propagate.
    Task<NotificationDeliveryResult> DeliverAsync(string recipientEmail, CancellationToken cancellationToken);
}

internal enum NotificationDeliveryResult
{
    Sent,
    Failed
}
