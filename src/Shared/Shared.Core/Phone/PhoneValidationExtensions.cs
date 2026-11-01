using FluentValidation;
using Shared.Core.Phone.Features.ValidatePhone;

namespace Shared.Core.Phone;

public static class PhoneValidationExtensions
{
    public static IServiceCollection AddPhoneValidationFeature(this IServiceCollection services)
    {
        services.AddSingleton<IPhoneValidationService, PhoneValidationService>();

        // Use raw MediatR registration — do NOT call the project's AddMediatR wrapper
        // to avoid re-registering ValidationBehavior/LoggingBehavior pipeline behaviors.
        var assembly = typeof(ValidatePhoneHandler).Assembly;
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
