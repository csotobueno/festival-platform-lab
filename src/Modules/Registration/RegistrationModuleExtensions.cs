using Festival.Modules.Registration.Application;
using Festival.Modules.Registration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Festival.Modules.Registration;

public static class RegistrationModuleExtensions
{
    public static IServiceCollection AddRegistrationModule(
        this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<RegistrationDbContext>(options =>
            RegistrationDbContext.Configure(options, connectionString));
        services.AddScoped<IRegistrationRepository, EfRegistrationRepository>();
        services.AddScoped<RegisterAttendeeUseCase>();
        return services;
    }
}
