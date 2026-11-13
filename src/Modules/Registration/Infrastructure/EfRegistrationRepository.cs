using Festival.Modules.Registration.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Festival.Modules.Registration.Infrastructure;

internal sealed class EfRegistrationRepository(RegistrationDbContext context) : IRegistrationRepository
{
    public Task<bool> ExistsByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        context.Registrations.AnyAsync(x => x.AttendeeDetails.Email == normalizedEmail, cancellationToken);

    public async Task<AddRegistrationResult> AddAsync(
        Domain.Registration registration, CancellationToken cancellationToken)
    {
        context.Registrations.Add(registration);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return AddRegistrationResult.Added;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                SchemaName: RegistrationDbContext.Schema,
                TableName: "registrations",
                ConstraintName: RegistrationDbContext.EmailUniqueIndex
            })
        {
            context.Entry(registration.AttendeeDetails).State = EntityState.Detached;
            context.Entry(registration).State = EntityState.Detached;
            return AddRegistrationResult.DuplicateEmail;
        }
    }
}
