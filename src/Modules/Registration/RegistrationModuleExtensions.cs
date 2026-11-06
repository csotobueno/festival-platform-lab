using Microsoft.Extensions.DependencyInjection;

namespace Festival.Modules.Registration;

public static class RegistrationModuleExtensions
{
    public static IServiceCollection AddRegistrationModule(this IServiceCollection services)
    {
        // Module services will be registered here when the first use case is implemented.
        return services;
    }
}
