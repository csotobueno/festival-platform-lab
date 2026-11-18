using Festival.Modules.Notifications.Application;
using Festival.Modules.Notifications.Domain;
using Festival.Modules.Notifications.Infrastructure;

namespace Festival.Modules.Notifications.Tests;

public sealed class RequestNotificationUseCaseTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delivery_outcome_is_committed_before_result_with_tokens_propagated(bool sent)
    {
        var repository = new TestRepository();
        var delivery = new TestDelivery(repository)
        {
            Outcome = sent ? NotificationDeliveryResult.Sent : NotificationDeliveryResult.Failed
        };
        using var cancellation = new CancellationTokenSource();
        var registrationId = Guid.NewGuid();

        var result = await new RequestNotificationUseCase(repository, delivery)
            .ExecuteAsync(registrationId, " jane@example.com ", cancellation.Token);

        var id = sent ? Assert.IsType<NotificationResult.Success>(result).NotificationId
            : Assert.IsType<NotificationResult.DeliveryFailed>(result).NotificationId;
        Assert.Equal(repository.Saved!.NotificationId, id);
        Assert.Equal(registrationId, repository.Saved.RegistrationId);
        Assert.Equal(sent ? NotificationStatus.Sent : NotificationStatus.Failed, repository.CommittedStatus);
        Assert.Equal(1, repository.FinalSaves);
        Assert.Equal("jane@example.com", delivery.Email);
        Assert.Equal(cancellation.Token, delivery.Token);
        Assert.Equal(cancellation.Token, repository.AddToken);
        Assert.Equal(cancellation.Token, repository.SaveToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task Invalid_email_returns_controlled_result_without_persistence_or_delivery(string? email)
    {
        var repository = new TestRepository();
        var delivery = new TestDelivery(repository);

        var result = await new RequestNotificationUseCase(repository, delivery)
            .ExecuteAsync(Guid.NewGuid(), email);

        Assert.IsType<NotificationResult.InvalidInput>(result);
        Assert.Null(repository.Saved);
        Assert.Null(delivery.Email);
    }

    [Fact]
    public async Task Empty_registration_id_returns_invalid_input_without_side_effects()
    {
        var repository = new TestRepository();
        var delivery = new TestDelivery(repository);

        var result = await new RequestNotificationUseCase(repository, delivery)
            .ExecuteAsync(Guid.Empty, "jane@example.com");

        Assert.IsType<NotificationResult.InvalidInput>(result);
        Assert.Null(repository.Saved);
        Assert.Null(delivery.Email);
    }

    [Fact]
    public async Task Initial_persistence_exception_propagates_and_prevents_delivery()
    {
        var error = new InvalidOperationException("Initial persistence failed");
        var repository = new TestRepository { AddError = error };
        var delivery = new TestDelivery(repository);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RequestNotificationUseCase(repository, delivery).ExecuteAsync(Guid.NewGuid(), "jane@example.com"));

        Assert.Same(error, thrown);
        Assert.Null(delivery.Email);
        Assert.Equal(0, repository.FinalSaves);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Final_persistence_exception_propagates_for_either_delivery_outcome(bool sent)
    {
        var error = new InvalidOperationException("Final persistence failed");
        var repository = new TestRepository { SaveError = error };
        var delivery = new TestDelivery(repository)
        {
            Outcome = sent ? NotificationDeliveryResult.Sent : NotificationDeliveryResult.Failed
        };

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RequestNotificationUseCase(repository, delivery).ExecuteAsync(Guid.NewGuid(), "jane@example.com"));

        Assert.Same(error, thrown);
        Assert.Equal(NotificationStatus.Pending, repository.CommittedStatus);
    }

    [Fact]
    public async Task Unexpected_delivery_error_propagates_without_marking_failed()
    {
        // Even ArgumentException from an adapter must not become an InvalidInput result.
        var error = new ArgumentException("Unexpected adapter error");
        var repository = new TestRepository();
        var delivery = new TestDelivery(repository) { Error = error };

        var thrown = await Assert.ThrowsAsync<ArgumentException>(() =>
            new RequestNotificationUseCase(repository, delivery).ExecuteAsync(Guid.NewGuid(), "jane@example.com"));

        Assert.Same(error, thrown);
        Assert.Equal(NotificationStatus.Pending, repository.Saved!.Status);
        Assert.Equal(0, repository.FinalSaves);
    }

    [Fact]
    public async Task Unknown_delivery_outcome_propagates_without_final_save()
    {
        var repository = new TestRepository();
        var delivery = new TestDelivery(repository) { Outcome = (NotificationDeliveryResult)99 };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RequestNotificationUseCase(repository, delivery).ExecuteAsync(Guid.NewGuid(), "jane@example.com"));

        Assert.Equal(NotificationStatus.Pending, repository.Saved!.Status);
        Assert.Equal(0, repository.FinalSaves);
    }

    [Fact]
    public async Task Precancelled_request_does_not_persist_or_deliver()
    {
        var repository = new TestRepository();
        var delivery = new TestDelivery(repository);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RequestNotificationUseCase(repository, delivery)
                .ExecuteAsync(Guid.NewGuid(), "jane@example.com", cancellation.Token));

        Assert.Null(repository.Saved);
        Assert.Null(delivery.Email);
    }

    [Fact]
    public async Task Delivery_cancellation_propagates_and_leaves_pending()
    {
        var repository = new TestRepository();
        var delivery = new TestDelivery(repository) { Error = new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RequestNotificationUseCase(repository, delivery).ExecuteAsync(Guid.NewGuid(), "jane@example.com"));

        Assert.Equal(NotificationStatus.Pending, repository.Saved!.Status);
        Assert.Equal(0, repository.FinalSaves);
    }

    [Fact]
    public async Task Simulated_adapter_reports_success()
    {
        var result = await new SimulatedNotificationDelivery().DeliverAsync("jane@example.com", default);

        Assert.Equal(NotificationDeliveryResult.Sent, result);
    }

    [Fact]
    public async Task Simulated_adapter_honors_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SimulatedNotificationDelivery().DeliverAsync("jane@example.com", cancellation.Token));
    }

    private sealed class TestRepository : INotificationRepository
    {
        public Notification? Saved { get; private set; }
        public NotificationStatus? CommittedStatus { get; private set; }
        public int FinalSaves { get; private set; }
        public Exception? AddError { get; init; }
        public Exception? SaveError { get; init; }
        public CancellationToken AddToken { get; private set; }
        public CancellationToken SaveToken { get; private set; }

        public Task AddAsync(Notification notification, CancellationToken cancellationToken)
        {
            if (AddError is not null) throw AddError;
            Assert.Equal(NotificationStatus.Pending, notification.Status);
            Saved = notification;
            CommittedStatus = notification.Status;
            AddToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (SaveError is not null) throw SaveError;
            FinalSaves++;
            CommittedStatus = Saved!.Status;
            SaveToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class TestDelivery(TestRepository repository) : INotificationDelivery
    {
        public NotificationDeliveryResult Outcome { get; init; } = NotificationDeliveryResult.Sent;
        public Exception? Error { get; init; }
        public string? Email { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<NotificationDeliveryResult> DeliverAsync(string recipientEmail, CancellationToken cancellationToken)
        {
            Assert.Equal(NotificationStatus.Pending, repository.CommittedStatus);
            Assert.Equal(0, repository.FinalSaves);
            Email = recipientEmail;
            Token = cancellationToken;
            if (Error is not null) throw Error;
            return Task.FromResult(Outcome);
        }
    }
}
