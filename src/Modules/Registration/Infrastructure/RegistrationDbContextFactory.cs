using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Festival.Modules.Registration.Infrastructure;

internal sealed class RegistrationDbContextFactory : IDesignTimeDbContextFactory<RegistrationDbContext>
{
    public RegistrationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Registration")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Registration before running EF tools.");

        var options = new DbContextOptionsBuilder<RegistrationDbContext>();
        RegistrationDbContext.Configure(options, connectionString);
        return new RegistrationDbContext(options.Options);
    }
}
