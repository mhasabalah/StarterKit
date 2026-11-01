namespace Shared.Core.Phone;

public static class PhoneValidatorExtensions
{
    /// <summary>
    /// Validates the phone number against a fixed country code.
    /// Use "ZZ" to require E.164 format (number must start with '+').
    /// </summary>
    public static IRuleBuilderOptions<T, string?> ValidPhoneFor<T>(
        this IRuleBuilder<T, string?> ruleBuilder,
        string countryCode,
        IPhoneValidationService phoneService)
    {
        return ruleBuilder
            .Must(phone => string.IsNullOrEmpty(phone) || phoneService.Validate(phone, countryCode).IsValid)
            .WithMessage(countryCode.Equals("ZZ", StringComparison.OrdinalIgnoreCase)
                ? "Phone number must be in international format (+country_code number)."
                : $"Phone number is not valid for the selected country ({countryCode}).");
    }

    /// <summary>
    /// Validates the phone number against a country code resolved from the command.
    /// Use when the country code is on the same command object.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> ValidPhoneFor<T>(
        this IRuleBuilder<T, string?> ruleBuilder,
        Func<T, string> getCountryCode,
        IPhoneValidationService phoneService)
    {
        return ruleBuilder
            .Must((instance, phone) =>
                string.IsNullOrEmpty(phone)
                || phoneService.Validate(phone, getCountryCode(instance)).IsValid)
            .WithMessage("Invalid phone number for the selected country.");
    }
}
