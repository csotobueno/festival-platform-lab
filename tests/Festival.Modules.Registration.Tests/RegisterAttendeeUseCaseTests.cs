using Festival.Modules.Notifications.Contracts;
using Festival.Modules.Registration.Application;
using Festival.Modules.Registration.Domain;
using Microsoft.Extensions.Logging;
using RegistrationAggregate = Festival.Modules.Registration.Domain.Registration;

namespace Festival.Modules.Registration.Tests;

public sealed class RegisterAttendeeUseCaseTests
{
    [Fact]
    public async Task Valid_input_is_normalized_and_persisted_with_cancellation_token()
    {
        var repository = new TestRepository();
        var notifications = new TestNotifications(repository);
        using var cancellation = new CancellationTokenSource();

        var result = await new RegisterAttendeeUseCase(repository, notifications, new TestLogger())
            .ExecuteAsync(" Jane Doe ", " Jane@EXAMPLE.com ", cancellation.Token);

        var success = Assert.IsType<RegisterAttendeeResult.Success>(result);
        Assert.Equal(repository.Saved!.RegistrationId, success.RegistrationId);
        Assert.Equal("Jane Doe", repository.Saved.AttendeeDetails.Name);
        Assert.Equal("jane@example.com", repository.CheckedEmail);
        Assert.Equal("jane@example.com", repository.Saved.AttendeeDetails.Email);
        Assert.Equal(cancellation.Token, repository.ReadToken);
        Assert.Equal(cancellation.Token, repository.WriteToken);
        var request = Assert.Single(notifications.Requests);
        Assert.Equal(success.RegistrationId, request.RegistrationId);
        Assert.Equal("jane@example.com", request.RecipientEmail);
        Assert.Equal(cancellation.Token, notifications.Token);
    }

    [Theory]
    [InlineData(null, "jane@example.com")]
    [InlineData(" ", "jane@example.com")]
    [InlineData("Jane", null)]
    [InlineData("Jane", "invalid")]
    public async Task Invalid_input_does_not_access_repository(string? name, string? email)
    {
        var repository = new TestRepository();
        var notifications = new TestNotifications(repository);

        var result = await new RegisterAttendeeUseCase(repository, notifications, new TestLogger()).ExecuteAsync(name, email);

        Assert.IsType<RegisterAttendeeResult.InvalidInput>(result);
        Assert.Null(repository.CheckedEmail);
        Assert.Null(repository.Saved);
        Assert.Empty(notifications.Requests);
    }

    [Fact]
    public async Task Existing_normalized_email_is_rejected_without_insert()
    {
        var repository = new TestRepository { Exists = true };
        var notifications = new TestNotifications(repository);

        var result = await new RegisterAttendeeUseCase(repository, notifications, new TestLogger())
            .ExecuteAsync("Jane", " Jane@EXAMPLE.COM ");

        Assert.IsType<RegisterAttendeeResult.DuplicateEmail>(result);
        Assert.Equal("jane@example.com", repository.CheckedEmail);
        Assert.Null(repository.Saved);
        Assert.Empty(notifications.Requests);
    }

    [Fact]
    public async Task Concurrent_duplicate_reported_by_persistence_is_a_conflict()
    {
        var repository = new TestRepository { InsertResult = AddRegistrationResult.DuplicateEmail };
        var notifications = new TestNotifications(repository);

        var result = await new RegisterAttendeeUseCase(repository, notifications, new TestLogger()).ExecuteAsync("Jane", "jane@example.com");

        Assert.IsType<RegisterAttendeeResult.DuplicateEmail>(result);
        Assert.NotNull(repository.Saved);
        Assert.Empty(notifications.Requests);
    }

    [Fact]
    public async Task Unexpected_repository_failure_propagates()
    {
        var error = new InvalidOperationException("Storage failed");
        var repository = new TestRepository { Error = error };
        var notifications = new TestNotifications(repository);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RegisterAttendeeUseCase(repository, notifications, new TestLogger()).ExecuteAsync("Jane", "jane@example.com"));

        Assert.Same(error, thrown);
        Assert.Empty(notifications.Requests);
    }

    [Fact]
    public async Task Cancelled_request_does_not_access_repository()
    {
        var repository = new TestRepository();
        var notifications = new TestNotifications(repository);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RegisterAttendeeUseCase(repository, notifications, new TestLogger()).ExecuteAsync("Jane", "jane@example.com", cancellation.Token));

        Assert.Null(repository.CheckedEmail);
        Assert.Null(repository.Saved);
        Assert.Empty(notifications.Requests);
    }

    [Theory]
    [InlineData(NotificationRequestResult.DeliveryFailed)]
    [InlineData(NotificationRequestResult.InvalidInput)]
    public async Task Controlled_notification_failure_is_logged_without_invalidating_registration(NotificationRequestResult outcome)
    {
        var repository = new TestRepository();
        var notifications = new TestNotifications(repository) { Outcome = outcome };
        var logger = new TestLogger();

        var result = await new RegisterAttendeeUseCase(repository, notifications, logger)
            .ExecuteAsync("Jane", "jane@example.com");

        var success = Assert.IsType<RegisterAttendeeResult.Success>(result);
        Assert.Equal(RegistrationStatus.Confirmed, repository.Saved!.Status);
        Assert.Single(notifications.Requests);
        AssertFailureLog(logger, success.RegistrationId, outcome.ToString(), null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unexpected_notification_error_including_cancellation_is_isolated_and_logged(bool cancelled)
    {
        var error = cancelled ? new OperationCanceledException() : (Exception)new InvalidOperationException("Storage failed");
        var repository = new TestRepository();
        var notifications = new TestNotifications(repository) { Error = error };
        var logger = new TestLogger();

        var result = await new RegisterAttendeeUseCase(repository, notifications, logger)
            .ExecuteAsync("Jane", "jane@example.com");

        var success = Assert.IsType<RegisterAttendeeResult.Success>(result);
        Assert.Equal(RegistrationStatus.Confirmed, repository.Saved!.Status);
        Assert.Single(notifications.Requests);
        AssertFailureLog(logger, success.RegistrationId, "UnexpectedError", error);
    }

    [Fact]
    public async Task Lookup_failure_propagates_without_notification()
    {
        var error = new InvalidOperationException("Lookup failed");
        var repository = new TestRepository { ReadError = error };
        var notifications = new TestNotifications(repository);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RegisterAttendeeUseCase(repository, notifications, new TestLogger())
                .ExecuteAsync("Jane", "jane@example.com"));

        Assert.Same(error, thrown);
        Assert.Empty(notifications.Requests);
    }

    [Fact]
    public async Task Notification_waits_until_registration_persistence_completes()
    {
        var commit = new TaskCompletionSource<AddRegistrationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new TestRepository { Commit = commit.Task };
        var notifications = new TestNotifications(repository);

        var registration = new RegisterAttendeeUseCase(repository, notifications, new TestLogger())
            .ExecuteAsync("Jane", "jane@example.com");

        Assert.False(registration.IsCompleted);
        Assert.Empty(notifications.Requests);
        commit.SetResult(AddRegistrationResult.Added);
        Assert.IsType<RegisterAttendeeResult.Success>(await registration);
        Assert.Single(notifications.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logging_provider_failure_does_not_invalidate_committed_registration(bool unexpected)
    {
        var repository = new TestRepository();
        var notifications = new TestNotifications(repository)
        {
            Outcome = NotificationRequestResult.DeliveryFailed,
            Error = unexpected ? new InvalidOperationException("Notification failed") : null
        };
        var logger = new TestLogger { Error = new InvalidOperationException("Logging failed") };

        var result = await new RegisterAttendeeUseCase(repository, notifications, logger)
            .ExecuteAsync("Jane", "jane@example.com");

        Assert.IsType<RegisterAttendeeResult.Success>(result);
        Assert.Equal(RegistrationStatus.Confirmed, repository.Saved!.Status);
        Assert.Equal(1, logger.Calls);
    }

    private static void AssertFailureLog(TestLogger logger, Guid registrationId, string category, Exception? error)
    {
        Assert.Equal(1, logger.Calls);
        Assert.Equal(LogLevel.Error, logger.Level);
        Assert.Equal(6001, logger.Event.Id);
        Assert.Equal("RegistrationConfirmationNotificationFailed", logger.Event.Name);
        Assert.Equal(registrationId, logger.State!["RegistrationId"]);
        Assert.Equal("RequestRegistrationConfirmation", logger.State["Operation"]);
        Assert.Equal(category, logger.State["FailureCategory"]);
        Assert.Same(error, logger.Exception);
        Assert.DoesNotContain("jane@example.com", string.Join(" ", logger.State.Values));
    }

    private sealed class TestNotifications(TestRepository repository) : INotificationRequestService
    {
        public List<NotificationRequest> Requests { get; } = [];
        public CancellationToken Token { get; private set; }
        public NotificationRequestResult Outcome { get; init; } = NotificationRequestResult.Success;
        public Exception? Error { get; init; }

        public Task<NotificationRequestResult> RequestAsync(NotificationRequest request, CancellationToken cancellationToken)
        {
            Assert.NotNull(repository.Saved);
            Assert.Equal(AddRegistrationResult.Added, repository.InsertResult);
            Assert.True(repository.Commit is null || repository.Commit.IsCompletedSuccessfully);
            Requests.Add(request);
            Token = cancellationToken;
            if (Error is not null) throw Error;
            return Task.FromResult(Outcome);
        }
    }

    private sealed class TestLogger : ILogger<RegisterAttendeeUseCase>
    {
        public Exception? Error { get; init; }
        public int Calls { get; private set; }
        public LogLevel Level { get; private set; }
        public EventId Event { get; private set; }
        public Dictionary<string, object?>? State { get; private set; }
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Calls++;
            if (Error is not null) throw Error;
            Level = logLevel;
            Event = eventId;
            State = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary();
            Exception = exception;
        }
    }

    private sealed class TestRepository : IRegistrationRepository
    {
        public bool Exists { get; init; }
        public AddRegistrationResult InsertResult { get; init; } = AddRegistrationResult.Added;
        public Exception? Error { get; init; }
        public Exception? ReadError { get; init; }
        public Task<AddRegistrationResult>? Commit { get; init; }
        public string? CheckedEmail { get; private set; }
        public RegistrationAggregate? Saved { get; private set; }
        public CancellationToken ReadToken { get; private set; }
        public CancellationToken WriteToken { get; private set; }

        public Task<bool> ExistsByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            if (ReadError is not null) throw ReadError;
            CheckedEmail = normalizedEmail;
            ReadToken = cancellationToken;
            return Task.FromResult(Exists);
        }

        public Task<AddRegistrationResult> AddAsync(RegistrationAggregate registration, CancellationToken cancellationToken)
        {
            if (Error is not null) throw Error;
            Saved = registration;
            WriteToken = cancellationToken;
            return Commit ?? Task.FromResult(InsertResult);
        }
    }
}
