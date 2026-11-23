using Festival.Modules.Notifications.Contracts;

namespace Festival.Modules.Notifications.Application;

internal sealed class NotificationRequestService(RequestNotificationUseCase useCase) : INotificationRequestService
{
    public async Task<NotificationRequestResult> RequestAsync(
        NotificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The existing use case owns validation, persistence, and delivery rules.
        var result = await useCase.ExecuteAsync(request.RegistrationId, request.RecipientEmail, cancellationToken);
        return result switch
        {
            NotificationResult.Success => NotificationRequestResult.Success,
            NotificationResult.DeliveryFailed => NotificationRequestResult.DeliveryFailed,
            NotificationResult.InvalidInput => NotificationRequestResult.InvalidInput,
            _ => throw new InvalidOperationException("Unknown notification result.")
        };
    }
}
