using Festival.Modules.Registration.Domain;

namespace Festival.Modules.Registration.Application;

public sealed class RegisterAttendeeUseCase(IRegistrationRepository repository)
{
    public async Task<RegisterAttendeeResult> ExecuteAsync(
        string? name, string? email, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AttendeeDetails attendee;
        try
        {
            attendee = new AttendeeDetails(name, email);
        }
        catch (ArgumentException exception)
        {
            return new RegisterAttendeeResult.InvalidInput(exception.Message);
        }

        if (await repository.ExistsByEmailAsync(attendee.Email, cancellationToken))
        {
            return new RegisterAttendeeResult.DuplicateEmail();
        }

        var registration = Domain.Registration.Create(attendee);
        var result = await repository.AddAsync(registration, cancellationToken);

        return result switch
        {
            AddRegistrationResult.Added => new RegisterAttendeeResult.Success(registration.RegistrationId),
            AddRegistrationResult.DuplicateEmail => new RegisterAttendeeResult.DuplicateEmail(),
            _ => throw new InvalidOperationException("Unknown persistence result.")
        };
    }
}

public abstract record RegisterAttendeeResult
{
    private RegisterAttendeeResult() { }

    public sealed record Success(Guid RegistrationId) : RegisterAttendeeResult;
    public sealed record InvalidInput(string Error) : RegisterAttendeeResult;
    public sealed record DuplicateEmail : RegisterAttendeeResult;
}
