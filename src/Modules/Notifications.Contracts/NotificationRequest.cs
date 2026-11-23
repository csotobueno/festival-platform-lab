namespace Festival.Modules.Notifications.Contracts;

public sealed record NotificationRequest(Guid RegistrationId, string RecipientEmail);
