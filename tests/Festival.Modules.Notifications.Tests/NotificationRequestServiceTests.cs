using Festival.Modules.Notifications.Application;
using Festival.Modules.Notifications.Contracts;
using Festival.Modules.Notifications.Domain;

namespace Festival.Modules.Notifications.Tests;

public sealed class NotificationRequestServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Contract_translates_request_and_committed_delivery_result(bool sent)
    {
        var repository = new TestRepository();
        var delivery = new TestDelivery(sent);
        INotificationRequestService service = new NotificationRequestService(new RequestNotificationUseCase(repository, delivery));
        using var cancellation = new CancellationTokenSource();
        var id = Guid.NewGuid();

        var result = await service.RequestAsync(new NotificationRequest(id, " Jane@EXAMPLE.com "), cancellation.Token);

        Assert.Equal(sent ? NotificationRequestResult.Success : NotificationRequestResult.DeliveryFailed, result);
        Assert.Equal(id, repository.Notification!.RegistrationId);
        Assert.Equal("Jane@EXAMPLE.com", repository.Notification.RecipientEmail);
        Assert.Equal(sent ? NotificationStatus.Sent : NotificationStatus.Failed, repository.CommittedStatus);
        Assert.Equal(cancellation.Token, repository.Token);
        Assert.Equal(cancellation.Token, delivery.Token);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Contract_validation_returns_invalid_input_without_side_effects(bool emptyId)
    {
        var repository = new TestRepository();
        var delivery = new TestDelivery(true);
        var service = new NotificationRequestService(new RequestNotificationUseCase(repository, delivery));

        var result = await service.RequestAsync(
            new NotificationRequest(emptyId ? Guid.Empty : Guid.NewGuid(), emptyId ? "jane@example.com" : "invalid"), default);

        Assert.Equal(NotificationRequestResult.InvalidInput, result);
        Assert.Null(repository.Notification);
        Assert.False(delivery.Called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Infrastructure_errors_propagate_without_becoming_delivery_failures(bool finalSave)
    {
        var error = new InvalidOperationException("Persistence failed");
        var repository = new TestRepository { Error = error, FailFinalSave = finalSave };
        var service = new NotificationRequestService(new RequestNotificationUseCase(repository, new TestDelivery(true)));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RequestAsync(new NotificationRequest(Guid.NewGuid(), "jane@example.com"), default));

        Assert.Same(error, thrown);
    }

    private sealed class TestRepository : INotificationRepository
    {
        public Notification? Notification { get; private set; }
        public NotificationStatus? CommittedStatus { get; private set; }
        public CancellationToken Token { get; private set; }
        public Exception? Error { get; init; }
        public bool FailFinalSave { get; init; }

        public Task AddAsync(Notification notification, CancellationToken cancellationToken)
        {
            if (Error is not null && !FailFinalSave) throw Error;
            Notification = notification;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (Error is not null && FailFinalSave) throw Error;
            CommittedStatus = Notification!.Status;
            Token = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class TestDelivery(bool sent) : INotificationDelivery
    {
        public bool Called { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<NotificationDeliveryResult> DeliverAsync(string recipientEmail, CancellationToken cancellationToken)
        {
            Called = true;
            Token = cancellationToken;
            return Task.FromResult(sent ? NotificationDeliveryResult.Sent : NotificationDeliveryResult.Failed);
        }
    }
}
