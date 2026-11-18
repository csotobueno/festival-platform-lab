namespace Festival.Modules.Notifications.Application;

internal abstract record NotificationResult
{
    private NotificationResult() { }

    public sealed record Success(Guid NotificationId) : NotificationResult;
    public sealed record DeliveryFailed(Guid NotificationId) : NotificationResult;
    public sealed record InvalidInput(string Error) : NotificationResult;
}
