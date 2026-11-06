using Festival.Modules.Notifications;
using Festival.Modules.Registration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRegistrationModule();
builder.Services.AddNotificationsModule();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok());

app.Run();
