namespace Festival.Modules.Registration.Application;

public interface IRegistrationRepository
{
    Task<bool> ExistsByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<AddRegistrationResult> AddAsync(Domain.Registration registration, CancellationToken cancellationToken);
}

public enum AddRegistrationResult
{
    Added,
    DuplicateEmail
}
