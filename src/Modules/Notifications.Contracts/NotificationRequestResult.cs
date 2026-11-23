namespace Festival.Modules.Notifications.Contracts;

public enum NotificationRequestResult
{
    // Both terminal outcomes are persisted before returning.
    Success,
    DeliveryFailed,
    InvalidInput
}
