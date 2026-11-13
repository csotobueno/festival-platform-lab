using Microsoft.EntityFrameworkCore;

namespace Festival.Modules.Registration.Infrastructure;

internal sealed class RegistrationDbContext(DbContextOptions<RegistrationDbContext> options) : DbContext(options)
{
    internal const string Schema = "registration";
    internal const string EmailUniqueIndex = "ux_registrations_email";

    public DbSet<Domain.Registration> Registrations => Set<Domain.Registration>();

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, postgres =>
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var registration = modelBuilder.Entity<Domain.Registration>();
        registration.ToTable("registrations", Schema);
        registration.HasKey(x => x.RegistrationId);
        registration.Property(x => x.RegistrationId).HasColumnName("registration_id").ValueGeneratedNever();
        registration.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        registration.Property(x => x.RegisteredAt).HasColumnName("registered_at").IsRequired();

        registration.OwnsOne(x => x.AttendeeDetails, attendee =>
        {
            attendee.Property(x => x.Name).HasColumnName("name").IsRequired();
            attendee.Property(x => x.Email).HasColumnName("email").IsRequired();
            attendee.HasIndex(x => x.Email).IsUnique().HasDatabaseName(EmailUniqueIndex);
        });
        registration.Navigation(x => x.AttendeeDetails).IsRequired();
    }
}
