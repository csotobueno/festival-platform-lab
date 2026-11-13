using Festival.Modules.Registration.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Festival.Modules.Registration.Presentation;

public static class RegistrationEndpoints
{
    public static IEndpointRouteBuilder MapRegistrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/registrations", async (
            RegisterAttendeeRequest request, RegisterAttendeeUseCase useCase, CancellationToken cancellationToken) =>
        {
            var result = await useCase.ExecuteAsync(request.Name, request.Email, cancellationToken);
            return result switch
            {
                RegisterAttendeeResult.Success success => Results.Created(
                    (string?)null, new RegistrationResponse(success.RegistrationId)),
                RegisterAttendeeResult.InvalidInput invalid => Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest, title: "Invalid registration", detail: invalid.Error),
                RegisterAttendeeResult.DuplicateEmail => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict, title: "Email already registered",
                    detail: "A registration already exists for this email address."),
                _ => throw new InvalidOperationException("Unknown registration result.")
            };
        });

        return endpoints;
    }
}

public sealed record RegisterAttendeeRequest(string? Name, string? Email);
public sealed record RegistrationResponse(Guid RegistrationId);
