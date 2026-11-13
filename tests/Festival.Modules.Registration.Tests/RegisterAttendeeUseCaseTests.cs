using Festival.Modules.Registration.Application;
using RegistrationAggregate = Festival.Modules.Registration.Domain.Registration;

namespace Festival.Modules.Registration.Tests;

public sealed class RegisterAttendeeUseCaseTests
{
    [Fact]
    public async Task Valid_input_is_normalized_and_persisted_with_cancellation_token()
    {
        var repository = new TestRepository();
        using var cancellation = new CancellationTokenSource();

        var result = await new RegisterAttendeeUseCase(repository)
            .ExecuteAsync(" Jane Doe ", " Jane@EXAMPLE.com ", cancellation.Token);

        var success = Assert.IsType<RegisterAttendeeResult.Success>(result);
        Assert.Equal(repository.Saved!.RegistrationId, success.RegistrationId);
        Assert.Equal("Jane Doe", repository.Saved.AttendeeDetails.Name);
        Assert.Equal("jane@example.com", repository.CheckedEmail);
        Assert.Equal("jane@example.com", repository.Saved.AttendeeDetails.Email);
        Assert.Equal(cancellation.Token, repository.ReadToken);
        Assert.Equal(cancellation.Token, repository.WriteToken);
    }

    [Theory]
    [InlineData(null, "jane@example.com")]
    [InlineData(" ", "jane@example.com")]
    [InlineData("Jane", null)]
    [InlineData("Jane", "invalid")]
    public async Task Invalid_input_does_not_access_repository(string? name, string? email)
    {
        var repository = new TestRepository();

        var result = await new RegisterAttendeeUseCase(repository).ExecuteAsync(name, email);

        Assert.IsType<RegisterAttendeeResult.InvalidInput>(result);
        Assert.Null(repository.CheckedEmail);
        Assert.Null(repository.Saved);
    }

    [Fact]
    public async Task Existing_normalized_email_is_rejected_without_insert()
    {
        var repository = new TestRepository { Exists = true };

        var result = await new RegisterAttendeeUseCase(repository)
            .ExecuteAsync("Jane", " Jane@EXAMPLE.COM ");

        Assert.IsType<RegisterAttendeeResult.DuplicateEmail>(result);
        Assert.Equal("jane@example.com", repository.CheckedEmail);
        Assert.Null(repository.Saved);
    }

    [Fact]
    public async Task Concurrent_duplicate_reported_by_persistence_is_a_conflict()
    {
        var repository = new TestRepository { InsertResult = AddRegistrationResult.DuplicateEmail };

        var result = await new RegisterAttendeeUseCase(repository).ExecuteAsync("Jane", "jane@example.com");

        Assert.IsType<RegisterAttendeeResult.DuplicateEmail>(result);
        Assert.NotNull(repository.Saved);
    }

    [Fact]
    public async Task Unexpected_repository_failure_propagates()
    {
        var error = new InvalidOperationException("Storage failed");
        var repository = new TestRepository { Error = error };

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RegisterAttendeeUseCase(repository).ExecuteAsync("Jane", "jane@example.com"));

        Assert.Same(error, thrown);
    }

    [Fact]
    public async Task Cancelled_request_does_not_access_repository()
    {
        var repository = new TestRepository();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RegisterAttendeeUseCase(repository).ExecuteAsync("Jane", "jane@example.com", cancellation.Token));

        Assert.Null(repository.CheckedEmail);
        Assert.Null(repository.Saved);
    }

    private sealed class TestRepository : IRegistrationRepository
    {
        public bool Exists { get; init; }
        public AddRegistrationResult InsertResult { get; init; } = AddRegistrationResult.Added;
        public Exception? Error { get; init; }
        public string? CheckedEmail { get; private set; }
        public RegistrationAggregate? Saved { get; private set; }
        public CancellationToken ReadToken { get; private set; }
        public CancellationToken WriteToken { get; private set; }

        public Task<bool> ExistsByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            CheckedEmail = normalizedEmail;
            ReadToken = cancellationToken;
            return Task.FromResult(Exists);
        }

        public Task<AddRegistrationResult> AddAsync(RegistrationAggregate registration, CancellationToken cancellationToken)
        {
            if (Error is not null) throw Error;
            Saved = registration;
            WriteToken = cancellationToken;
            return Task.FromResult(InsertResult);
        }
    }
}
