using Festival.Modules.Notifications;
using Festival.Modules.Registration;
using Festival.Modules.Registration.Presentation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRegistrationModule(
    builder.Configuration.GetConnectionString("Registration")
    ?? throw new InvalidOperationException("ConnectionStrings:Registration must be configured."));
builder.Services.AddNotificationsModule();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError
});
app.MapRegistrationEndpoints();
app.MapGet("/health", () => Results.Ok());

app.Run();

public partial class Program;
