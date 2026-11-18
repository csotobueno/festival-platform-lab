using System.Net.Mail;

namespace Festival.Modules.Notifications.Domain;

internal sealed class Notification
{
    public Guid NotificationId { get; private set; }
    public Guid RegistrationId { get; private set; }
    public string RecipientEmail { get; private set; } = null!;
    public NotificationStatus Status { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }

    // EF Core reconstructs persisted state; callers use Create to enforce invariants.
    private Notification() { }

    public static Notification Create(Guid registrationId, string? recipientEmail)
    {
        if (registrationId == Guid.Empty)
        {
            throw new ArgumentException("Registration identifier is required.", nameof(registrationId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientEmail);

        var email = recipientEmail.Trim();
        if (email.Any(char.IsWhiteSpace)
            || !MailAddress.TryCreate(email, out var address)
            || address.Address != email)
        {
            throw new ArgumentException("Recipient email must be a valid email address.", nameof(recipientEmail));
        }

        return new Notification
        {
            NotificationId = Guid.NewGuid(),
            RegistrationId = registrationId,
            RecipientEmail = email,
            Status = NotificationStatus.Pending,
            RequestedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkAsSent() => Complete(NotificationStatus.Sent);

    public void MarkAsFailed() => Complete(NotificationStatus.Failed);

    private void Complete(NotificationStatus status)
    {
        if (Status != NotificationStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending notification can be completed.");
        }
        Status = status;
    }
}
