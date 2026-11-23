namespace Festival.Modules.Notifications.Contracts;

public interface INotificationRequestService
{
    // Persistence failures and unexpected errors propagate to the caller.
    Task<NotificationRequestResult> RequestAsync(
        NotificationRequest request, CancellationToken cancellationToken);
}
