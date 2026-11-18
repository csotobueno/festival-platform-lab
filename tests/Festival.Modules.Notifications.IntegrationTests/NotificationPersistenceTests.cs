using Festival.Modules.Notifications.Application;
using Festival.Modules.Notifications.Domain;
using Festival.Modules.Notifications.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Festival.Modules.Notifications.IntegrationTests;

public sealed class NotificationPersistenceTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = new NpgsqlConnectionStringBuilder(postgres.Container.GetConnectionString())
        {
            Database = $"notifications_test_{Guid.NewGuid():N}"
        }.ConnectionString;

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Pending_is_committed_before_delivery_and_final_outcome_is_loaded_from_new_context(bool sent)
    {
        await using var context = CreateContext();
        var registrationId = Guid.NewGuid();
        Notification? observed = null;
        var before = DateTimeOffset.UtcNow;
        var delivery = new TestDelivery(async cancellationToken =>
        {
            Assert.Null(context.Database.CurrentTransaction);
            // A different connection can see Pending, proving it was committed before delivery.
            await using var observer = CreateContext();
            observed = await observer.Notifications.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(NotificationStatus.Pending, observed.Status);
            return sent ? NotificationDeliveryResult.Sent : NotificationDeliveryResult.Failed;
        });

        var result = await new RequestNotificationUseCase(new EfNotificationRepository(context), delivery)
            .ExecuteAsync(registrationId, " Jane@EXAMPLE.com ");

        var id = sent ? Assert.IsType<NotificationResult.Success>(result).NotificationId
            : Assert.IsType<NotificationResult.DeliveryFailed>(result).NotificationId;
        await using var verification = CreateContext();
        var stored = await verification.Notifications.SingleAsync();
        Assert.Equal(id, stored.NotificationId);
        Assert.Equal(observed!.NotificationId, id);
        Assert.Equal(registrationId, stored.RegistrationId);
        Assert.Equal("Jane@EXAMPLE.com", stored.RecipientEmail);
        Assert.Equal(sent ? NotificationStatus.Sent : NotificationStatus.Failed, stored.Status);
        Assert.Equal(TimeSpan.Zero, stored.RequestedAt.Offset);
        Assert.InRange(stored.RequestedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow);
        Assert.False(verification.Database.HasPendingModelChanges());
        Assert.Throws<InvalidOperationException>(() => stored.MarkAsSent());
        Assert.Throws<InvalidOperationException>(() => stored.MarkAsFailed());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Loaded_pending_aggregate_can_be_updated_in_a_new_context(bool sent)
    {
        var notification = Notification.Create(Guid.NewGuid(), "jane@example.com");
        await using (var initial = CreateContext())
        {
            await new EfNotificationRepository(initial).AddAsync(notification, default);
        }
        await using (var update = CreateContext())
        {
            var loaded = await update.Notifications.SingleAsync();
            if (sent) loaded.MarkAsSent();
            else loaded.MarkAsFailed();
            await new EfNotificationRepository(update).SaveChangesAsync(default);
        }

        await using var verification = CreateContext();
        var stored = await verification.Notifications.SingleAsync();
        Assert.Equal(notification.NotificationId, stored.NotificationId);
        Assert.Equal(sent ? NotificationStatus.Sent : NotificationStatus.Failed, stored.Status);
    }

    [Fact]
    public async Task Initial_database_failure_prevents_delivery_and_propagates()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE notifications.notifications ADD CONSTRAINT reject_insert CHECK (false)");
        var delivery = new TestDelivery(_ => Task.FromResult(NotificationDeliveryResult.Sent));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
            new RequestNotificationUseCase(new EfNotificationRepository(context), delivery)
                .ExecuteAsync(Guid.NewGuid(), "jane@example.com"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        Assert.Equal(0, delivery.Calls);
        await using var verification = CreateContext();
        Assert.Empty(await verification.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Final_database_failure_propagates_and_preserves_committed_pending_state(bool sent)
    {
        await using var context = CreateContext();
        // A real PostgreSQL update error: inserts can commit Pending but terminal updates fail.
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE notifications.notifications ADD CONSTRAINT reject_completion CHECK (status = 'Pending')");
        var delivery = new TestDelivery(_ => Task.FromResult(
            sent ? NotificationDeliveryResult.Sent : NotificationDeliveryResult.Failed));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
            new RequestNotificationUseCase(new EfNotificationRepository(context), delivery)
                .ExecuteAsync(Guid.NewGuid(), "jane@example.com"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        Assert.Equal(1, delivery.Calls);
        await using var verification = CreateContext();
        Assert.Equal(NotificationStatus.Pending, (await verification.Notifications.SingleAsync()).Status);
    }

    [Fact]
    public async Task Unexpected_delivery_exception_propagates_and_preserves_pending()
    {
        await using var context = CreateContext();
        var error = new InvalidOperationException("Unexpected adapter failure");
        var delivery = new TestDelivery(_ => throw error);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RequestNotificationUseCase(new EfNotificationRepository(context), delivery)
                .ExecuteAsync(Guid.NewGuid(), "jane@example.com"));

        Assert.Same(error, thrown);
        await using var verification = CreateContext();
        Assert.Equal(NotificationStatus.Pending, (await verification.Notifications.SingleAsync()).Status);
    }

    [Fact]
    public async Task Module_registration_resolves_scoped_services_and_default_simulated_delivery()
    {
        var services = new ServiceCollection();
        services.AddNotificationsModule(connectionString);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        await using var otherScope = provider.CreateAsyncScope();

        var useCase = scope.ServiceProvider.GetRequiredService<RequestNotificationUseCase>();
        Assert.Same(useCase, scope.ServiceProvider.GetRequiredService<RequestNotificationUseCase>());
        Assert.NotSame(useCase, otherScope.ServiceProvider.GetRequiredService<RequestNotificationUseCase>());
        Assert.IsType<SimulatedNotificationDelivery>(scope.ServiceProvider.GetRequiredService<INotificationDelivery>());
        Assert.IsType<EfNotificationRepository>(scope.ServiceProvider.GetRequiredService<INotificationRepository>());
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.Same(context, scope.ServiceProvider.GetRequiredService<NotificationDbContext>());
        Assert.NotSame(context, otherScope.ServiceProvider.GetRequiredService<NotificationDbContext>());

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), "jane@example.com");

        var success = Assert.IsType<NotificationResult.Success>(result);
        await using var verification = CreateContext();
        Assert.Equal(NotificationStatus.Sent,
            (await verification.Notifications.SingleAsync(x => x.NotificationId == success.NotificationId)).Status);
        Assert.Empty(verification.Model.FindEntityType(typeof(Notification))!.GetForeignKeys());
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM notifications.\"__EFMigrationsHistory\"", connection);
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    private NotificationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>();
        NotificationDbContext.Configure(options, connectionString);
        return new NotificationDbContext(options.Options);
    }

    private sealed class TestDelivery(Func<CancellationToken, Task<NotificationDeliveryResult>> deliver)
        : INotificationDelivery
    {
        public int Calls { get; private set; }

        public Task<NotificationDeliveryResult> DeliverAsync(string recipientEmail, CancellationToken cancellationToken)
        {
            Calls++;
            return deliver(cancellationToken);
        }
    }
}
