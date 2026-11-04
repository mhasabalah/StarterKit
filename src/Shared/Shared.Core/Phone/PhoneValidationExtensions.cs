using FluentValidation;
using Shared.Core.Extensions;
using Shared.Core.Phone.Features.ValidatePhone;

namespace Shared.Core.Phone;

public static class PhoneValidationExtensions
{
    public static IServiceCollection AddPhoneValidationFeature(this IServiceCollection services)
    {
        services.AddSingleton<IPhoneValidationService, PhoneValidationService>();

        // Handlers only — do NOT call AddMediator here
        // to avoid re-registering ValidationBehavior/LoggingBehavior pipeline behaviors.
        var assembly = typeof(ValidatePhoneHandler).Assembly;
        services.AddMediatorHandlers(assembly);
        // Skip open generic validators (e.g. BaseQueryValidator<TQuery>): they are base classes, not services.
        // Registering one makes every IValidator<T> resolution throw "Cannot instantiate implementation type".
        services.AddValidatorsFromAssembly(assembly,
            filter: r => !r.ValidatorType.IsGenericTypeDefinition, includeInternalTypes: true);

        return services;
    }
}
