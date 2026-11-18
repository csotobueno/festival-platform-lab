using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Festival.Modules.Notifications.Infrastructure;

internal sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Notifications")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Notifications before running EF tools.");

        var options = new DbContextOptionsBuilder<NotificationDbContext>();
        NotificationDbContext.Configure(options, connectionString);
        return new NotificationDbContext(options.Options);
    }
}
