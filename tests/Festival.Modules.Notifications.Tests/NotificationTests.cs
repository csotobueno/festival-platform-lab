using Festival.Modules.Notifications.Domain;

namespace Festival.Modules.Notifications.Tests;

public sealed class NotificationTests
{
    [Fact]
    public void Valid_creation_generates_identity_and_pending_state_with_utc_timestamp()
    {
        var registrationId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        var notification = Notification.Create(registrationId, " Jane@EXAMPLE.com ");

        Assert.NotEqual(Guid.Empty, notification.NotificationId);
        Assert.NotEqual(notification.NotificationId,
            Notification.Create(registrationId, "Jane@EXAMPLE.com").NotificationId);
        Assert.Equal(registrationId, notification.RegistrationId);
        Assert.Equal("Jane@EXAMPLE.com", notification.RecipientEmail);
        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal(TimeSpan.Zero, notification.RequestedAt.Offset);
        Assert.InRange(notification.RequestedAt, before, DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("invalid")]
    [InlineData("jane @example.com")]
    [InlineData("Jane <jane@example.com>")]
    [InlineData("jane@example.com,other@example.com")]
    public void Invalid_recipient_is_rejected(string? email) =>
        Assert.ThrowsAny<ArgumentException>(() => Notification.Create(Guid.NewGuid(), email));

    [Fact]
    public void Empty_registration_identifier_is_rejected() =>
        Assert.Throws<ArgumentException>(() => Notification.Create(Guid.Empty, "jane@example.com"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pending_notification_can_be_completed(bool sent)
    {
        var notification = Notification.Create(Guid.NewGuid(), "jane@example.com");

        if (sent) notification.MarkAsSent();
        else notification.MarkAsFailed();

        Assert.Equal(sent ? NotificationStatus.Sent : NotificationStatus.Failed, notification.Status);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Terminal_state_rejects_repeated_or_different_completion(bool initiallySent, bool nextSent)
    {
        var notification = Notification.Create(Guid.NewGuid(), "jane@example.com");
        if (initiallySent) notification.MarkAsSent();
        else notification.MarkAsFailed();
        var terminalStatus = notification.Status;

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (nextSent) notification.MarkAsSent();
            else notification.MarkAsFailed();
        });

        Assert.Equal(terminalStatus, notification.Status);
    }
}
