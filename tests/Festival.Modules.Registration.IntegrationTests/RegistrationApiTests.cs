using System.Net;
using System.Net.Http.Json;
using System.Text;
using Festival.Modules.Notifications.Application;
using Festival.Modules.Notifications.Domain;
using Festival.Modules.Notifications.Infrastructure;
using Festival.Modules.Registration.Application;
using Festival.Modules.Registration.Domain;
using Festival.Modules.Registration.Infrastructure;
using Festival.Modules.Registration.Presentation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using RegistrationAggregate = Festival.Modules.Registration.Domain.Registration;

namespace Festival.Modules.Registration.IntegrationTests;

public sealed class RegistrationApiTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private string connectionString = null!;
    private WebApplicationFactory<Program>? factory;

    public async Task InitializeAsync()
    {
        connectionString = new NpgsqlConnectionStringBuilder(postgres.Container.GetConnectionString())
        {
            Database = $"registration_test_{Guid.NewGuid():N}"
        }.ConnectionString;

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await using var notifications = CreateNotificationContext();
        await notifications.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Valid_registration_returns_201_and_persists_confirmed_aggregate()
    {
        using var client = CreateClient();
        var before = DateTimeOffset.UtcNow;

        using var response = await client.PostAsJsonAsync("/registrations", new
        {
            name = " Jane Doe ", email = " Jane@EXAMPLE.com "
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegistrationResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.RegistrationId);
        await using var context = CreateContext();
        var stored = await context.Registrations.AsNoTracking().SingleAsync();
        Assert.Equal(body.RegistrationId, stored.RegistrationId);
        Assert.Equal(new AttendeeDetails("Jane Doe", "jane@example.com"), stored.AttendeeDetails);
        Assert.Equal(RegistrationStatus.Confirmed, stored.Status);
        Assert.Equal(TimeSpan.Zero, stored.RegisteredAt.Offset);
        Assert.InRange(stored.RegisteredAt, before.AddSeconds(-1), DateTimeOffset.UtcNow);
        Assert.False(context.Database.HasPendingModelChanges());
        await using var notifications = CreateNotificationContext();
        var notification = await notifications.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal(body.RegistrationId, notification.RegistrationId);
        Assert.Equal("jane@example.com", notification.RecipientEmail);
        Assert.Equal(NotificationStatus.Sent, notification.Status);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("registrationId", Assert.Single(json.RootElement.EnumerateObject()).Name);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":null,\"email\":\"jane@example.com\"}")]
    [InlineData("{\"name\":\"  \",\"email\":\"jane@example.com\"}")]
    [InlineData("{\"name\":\"Jane\"}")]
    [InlineData("{\"name\":\"Jane\",\"email\":\"invalid\"}")]
    [InlineData("null")]
    [InlineData("{malformed")]
    public async Task Invalid_input_returns_400_and_does_not_persist(string json)
    {
        using var client = CreateClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/registrations", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = CreateContext();
        Assert.Equal(0, await context.Registrations.CountAsync());
        await using var notifications = CreateNotificationContext();
        Assert.Empty(await notifications.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData("jane@example.com")]
    [InlineData(" JANE@EXAMPLE.COM ")]
    public async Task Duplicate_normalized_email_returns_409_without_extra_registration(string duplicateEmail)
    {
        using var client = CreateClient();
        using var original = await client.PostAsJsonAsync("/registrations", new { name = "Jane", email = "jane@example.com" });
        Assert.Equal(HttpStatusCode.Created, original.StatusCode);

        using var duplicate = await client.PostAsJsonAsync("/registrations", new { name = "Other", email = duplicateEmail });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var problem = await duplicate.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(409, problem!.Status);
        await using var context = CreateContext();
        Assert.Equal(1, await context.Registrations.CountAsync());
        await using var notifications = CreateNotificationContext();
        Assert.Single(await notifications.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_requests_that_both_pass_precheck_return_201_and_409()
    {
        var gate = new ConcurrentLookupGate();
        using var client = CreateClient(services => services.AddScoped<IRegistrationRepository>(provider =>
            new SynchronizedRepository(new EfRegistrationRepository(provider.GetRequiredService<RegistrationDbContext>()), gate)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/registrations", new { name = "Jane", email = "jane@example.com" }, timeout.Token),
            client.PostAsJsonAsync("/registrations", new { name = "Other", email = " JANE@EXAMPLE.COM " }, timeout.Token));

        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(2, gate.AbsentEmailChecks);
            await using var context = CreateContext();
            var stored = await context.Registrations.SingleAsync();
            Assert.Equal("jane@example.com", stored.AttendeeDetails.Email);
            await using var notifications = CreateNotificationContext();
            var notification = await notifications.Notifications.SingleAsync();
            Assert.Equal(stored.RegistrationId, notification.RegistrationId);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Repository_translates_only_email_index_violation_and_remains_usable()
    {
        await using var context = CreateContext();
        var repository = new EfRegistrationRepository(context);
        Assert.Equal(AddRegistrationResult.Added, await repository.AddAsync(
            RegistrationAggregate.Create(new AttendeeDetails("Jane", "jane@example.com")), default));

        var result = await repository.AddAsync(
            RegistrationAggregate.Create(new AttendeeDetails("Other", "JANE@example.com")), default);

        Assert.Equal(AddRegistrationResult.DuplicateEmail, result);
        Assert.Equal(AddRegistrationResult.Added, await repository.AddAsync(
            RegistrationAggregate.Create(new AttendeeDetails("Other", "other@example.com")), default));
        Assert.Equal(2, await context.Registrations.CountAsync());
    }

    [Fact]
    public async Task Unrelated_database_constraint_failure_is_not_swallowed()
    {
        var registration = RegistrationAggregate.Create(new AttendeeDetails("Jane", "jane@example.com"));
        await using (var original = CreateContext())
        {
            await new EfRegistrationRepository(original).AddAsync(registration, default);
            await original.Database.ExecuteSqlRawAsync(
                "UPDATE registration.registrations SET email = 'other@example.com'");
        }
        // The same technical identity violates the primary key, not the email index.
        await using var context = CreateContext();
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            new EfRegistrationRepository(context).AddAsync(registration, default));

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.NotEqual(RegistrationDbContext.EmailUniqueIndex, postgresException.ConstraintName);
    }

    [Fact]
    public async Task Unexpected_failure_returns_generic_500_even_in_development()
    {
        using var client = CreateClient(services => services.AddScoped<IRegistrationRepository, FailingRepository>());

        using var response = await client.PostAsJsonAsync("/registrations", new { name = "Jane", email = "jane@example.com" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-database-details", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(500, problem!.Status);
    }

    [Fact]
    public async Task Existing_health_endpoint_remains_available()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Expected_delivery_failure_preserves_confirmed_registration_and_records_failed_notification()
    {
        var delivery = new ControlledDelivery(async cancellationToken =>
        {
            // Fresh contexts observe both commits before delivery, with no shared transaction.
            await using var registrations = CreateContext();
            var registration = await registrations.Registrations.SingleAsync(cancellationToken);
            Assert.Equal(RegistrationStatus.Confirmed, registration.Status);
            await using var notifications = CreateNotificationContext();
            var notification = await notifications.Notifications.SingleAsync(cancellationToken);
            Assert.Equal(registration.RegistrationId, notification.RegistrationId);
            Assert.Equal(NotificationStatus.Pending, notification.Status);
            return NotificationDeliveryResult.Failed;
        });
        var logger = new IntegrationLogger();
        using var client = CreateClient(services =>
        {
            services.AddScoped<INotificationDelivery>(_ => delivery);
            services.AddSingleton<ILogger<RegisterAttendeeUseCase>>(logger);
        });

        using var response = await client.PostAsJsonAsync("/registrations", new { name = "Jane", email = "jane@example.com" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegistrationResponse>();
        await using var registrations = CreateContext();
        Assert.Equal(RegistrationStatus.Confirmed, (await registrations.Registrations.SingleAsync()).Status);
        await using var notifications = CreateNotificationContext();
        var notification = await notifications.Notifications.SingleAsync();
        Assert.Equal(body!.RegistrationId, notification.RegistrationId);
        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(1, delivery.Calls);
        Assert.Equal("DeliveryFailed", Assert.Single(logger.Entries).Category);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Notification_database_failure_is_logged_and_preserves_committed_registration(bool finalSave)
    {
        await using (var setup = CreateNotificationContext())
        {
            await setup.Database.ExecuteSqlRawAsync(finalSave
                ? "ALTER TABLE notifications.notifications ADD CONSTRAINT reject_completion CHECK (status = 'Pending')"
                : "ALTER TABLE notifications.notifications ADD CONSTRAINT reject_insert CHECK (false)");
        }
        var logger = new IntegrationLogger();
        var delivery = new ControlledDelivery(_ => Task.FromResult(NotificationDeliveryResult.Sent));
        using var client = CreateClient(services =>
        {
            services.AddSingleton<ILogger<RegisterAttendeeUseCase>>(logger);
            services.AddScoped<INotificationDelivery>(_ => delivery);
        });

        using var response = await client.PostAsJsonAsync("/registrations", new { name = "Jane", email = "jane@example.com" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegistrationResponse>();
        await using var registrations = CreateContext();
        var registration = await registrations.Registrations.SingleAsync();
        Assert.Equal(body!.RegistrationId, registration.RegistrationId);
        Assert.Equal(RegistrationStatus.Confirmed, registration.Status);
        await using var notifications = CreateNotificationContext();
        var rows = await notifications.Notifications.ToListAsync();
        if (finalSave)
        {
            var pending = Assert.Single(rows);
            Assert.Equal(NotificationStatus.Pending, pending.Status);
            Assert.Equal(registration.RegistrationId, pending.RegistrationId);
        }
        else Assert.Empty(rows);
        Assert.Equal(finalSave ? 1 : 0, delivery.Calls);
        var log = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, log.Level);
        Assert.Equal(6001, log.Event.Id);
        Assert.Equal(registration.RegistrationId, log.RegistrationId);
        Assert.Equal("RequestRegistrationConfirmation", log.Operation);
        Assert.Equal("UnexpectedError", log.Category);
        var exception = Assert.IsType<DbUpdateException>(log.Exception);
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        Assert.DoesNotContain("jane@example.com", log.Message);
        Assert.DoesNotContain("Exception", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Registration_database_failure_returns_500_without_notification()
    {
        await using (var setup = CreateContext())
        {
            await setup.Database.ExecuteSqlRawAsync(
                "ALTER TABLE registration.registrations ADD CONSTRAINT reject_insert CHECK (false)");
        }
        var logger = new IntegrationLogger();
        using var client = CreateClient(services => services.AddSingleton<ILogger<RegisterAttendeeUseCase>>(logger));

        using var response = await client.PostAsJsonAsync("/registrations", new { name = "Jane", email = "jane@example.com" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var notifications = CreateNotificationContext();
        Assert.Empty(await notifications.Notifications.ToListAsync());
        Assert.Empty(logger.Entries);
    }

    private HttpClient CreateClient(Action<IServiceCollection>? configure = null)
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Registration", connectionString);
            builder.UseSetting("ConnectionStrings:Notifications", connectionString);
            if (configure is not null) builder.ConfigureTestServices(configure);
        });
        return factory.CreateClient();
    }

    private RegistrationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RegistrationDbContext>();
        RegistrationDbContext.Configure(options, connectionString);
        return new RegistrationDbContext(options.Options);
    }

    private NotificationDbContext CreateNotificationContext()
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>();
        NotificationDbContext.Configure(options, connectionString);
        return new NotificationDbContext(options.Options);
    }

    private sealed class ControlledDelivery(Func<CancellationToken, Task<NotificationDeliveryResult>> deliver) : INotificationDelivery
    {
        public int Calls { get; private set; }
        public Task<NotificationDeliveryResult> DeliverAsync(string recipientEmail, CancellationToken cancellationToken)
        {
            Calls++;
            return deliver(cancellationToken);
        }
    }

    private sealed record IntegrationLog(LogLevel Level, EventId Event, Guid RegistrationId,
        string Operation, string Category, Exception? Exception, string Message);

    private sealed class IntegrationLogger : ILogger<RegisterAttendeeUseCase>
    {
        public List<IntegrationLog> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary();
            Entries.Add(new IntegrationLog(logLevel, eventId, (Guid)fields["RegistrationId"]!,
                (string)fields["Operation"]!, (string)fields["FailureCategory"]!, exception, formatter(state, exception)));
        }
    }

    private sealed class ConcurrentLookupGate
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int absentEmailChecks;
        public int AbsentEmailChecks => absentEmailChecks;

        public async Task WaitAsync(bool exists, CancellationToken cancellationToken)
        {
            Assert.False(exists);
            if (Interlocked.Increment(ref absentEmailChecks) == 2) ready.SetResult();
            await ready.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class SynchronizedRepository(IRegistrationRepository inner, ConcurrentLookupGate gate) : IRegistrationRepository
    {
        public async Task<bool> ExistsByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            var exists = await inner.ExistsByEmailAsync(normalizedEmail, cancellationToken);
            await gate.WaitAsync(exists, cancellationToken);
            return exists;
        }

        public Task<AddRegistrationResult> AddAsync(RegistrationAggregate registration, CancellationToken cancellationToken) =>
            inner.AddAsync(registration, cancellationToken);
    }

    private sealed class FailingRepository : IRegistrationRepository
    {
        public Task<bool> ExistsByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("private-database-details");

        public Task<AddRegistrationResult> AddAsync(RegistrationAggregate registration, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("private-database-details");
    }
}
