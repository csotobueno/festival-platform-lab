using Festival.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace Festival.Modules.Notifications.Infrastructure;

internal sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    internal const string Schema = "notifications";

    public DbSet<Notification> Notifications => Set<Notification>();

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, postgres =>
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var notification = modelBuilder.Entity<Notification>();
        notification.ToTable("notifications", Schema);
        notification.HasKey(x => x.NotificationId);
        notification.Property(x => x.NotificationId).HasColumnName("notification_id").ValueGeneratedNever();
        notification.Property(x => x.RegistrationId).HasColumnName("registration_id").IsRequired();
        notification.Property(x => x.RecipientEmail).HasColumnName("recipient_email").IsRequired();
        notification.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        notification.Property(x => x.RequestedAt).HasColumnName("requested_at").IsRequired();
    }
}
