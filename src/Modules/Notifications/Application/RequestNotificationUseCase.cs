using Festival.Modules.Notifications.Domain;

namespace Festival.Modules.Notifications.Application;

internal sealed class RequestNotificationUseCase(
    INotificationRepository repository, INotificationDelivery delivery)
{
    public async Task<NotificationResult> ExecuteAsync(
        Guid registrationId, string? recipientEmail, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Notification notification;
        try
        {
            notification = Notification.Create(registrationId, recipientEmail);
        }
        catch (ArgumentException exception)
        {
            return new NotificationResult.InvalidInput(exception.Message);
        }

        await repository.AddAsync(notification, cancellationToken);
        var result = await delivery.DeliverAsync(notification.RecipientEmail, cancellationToken);

        switch (result)
        {
            case NotificationDeliveryResult.Sent:
                notification.MarkAsSent();
                break;
            case NotificationDeliveryResult.Failed:
                notification.MarkAsFailed();
                break;
            default:
                throw new InvalidOperationException("Unknown delivery result.");
        }

        await repository.SaveChangesAsync(cancellationToken);

        return result == NotificationDeliveryResult.Sent
            ? new NotificationResult.Success(notification.NotificationId)
            : new NotificationResult.DeliveryFailed(notification.NotificationId);
    }
}
