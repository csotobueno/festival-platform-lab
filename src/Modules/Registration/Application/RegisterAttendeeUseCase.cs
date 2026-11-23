using Festival.Modules.Notifications.Contracts;
using Festival.Modules.Registration.Domain;
using Microsoft.Extensions.Logging;

namespace Festival.Modules.Registration.Application;

public sealed class RegisterAttendeeUseCase(
    IRegistrationRepository repository,
    INotificationRequestService notifications,
    ILogger<RegisterAttendeeUseCase> logger)
{
    private static readonly EventId NotificationFailure = new(6001, "RegistrationConfirmationNotificationFailed");

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

        switch (result)
        {
            case AddRegistrationResult.DuplicateEmail:
                return new RegisterAttendeeResult.DuplicateEmail();
            case AddRegistrationResult.Added:
                break;
            default:
                throw new InvalidOperationException("Unknown persistence result.");
        }

        // AddAsync has committed. Only notification failures are isolated here.
        try
        {
            var notificationResult = await notifications.RequestAsync(
                new NotificationRequest(registration.RegistrationId, attendee.Email), cancellationToken);
            if (notificationResult != NotificationRequestResult.Success)
            {
                LogNotificationFailure(registration.RegistrationId, notificationResult.ToString());
            }
        }
        catch (Exception exception)
        {
            // Includes cancellation after the Registration commit; never undo confirmed business state.
            LogNotificationFailure(registration.RegistrationId, "UnexpectedError", exception);
        }

        return new RegisterAttendeeResult.Success(registration.RegistrationId);
    }

    private void LogNotificationFailure(Guid registrationId, string failureCategory, Exception? exception = null)
    {
        try
        {
            logger.LogError(NotificationFailure, exception,
                "Notification integration failed for Registration {RegistrationId}. Operation {Operation}; category {FailureCategory}",
                registrationId, "RequestRegistrationConfirmation", failureCategory);
        }
        catch (Exception)
        {
            // A logging provider failure must not invalidate an already committed Registration either.
        }
    }
}

public abstract record RegisterAttendeeResult
{
    private RegisterAttendeeResult() { }

    public sealed record Success(Guid RegistrationId) : RegisterAttendeeResult;
    public sealed record InvalidInput(string Error) : RegisterAttendeeResult;
    public sealed record DuplicateEmail : RegisterAttendeeResult;
}
