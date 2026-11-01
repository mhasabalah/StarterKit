namespace Shared.Core.Phone;

public interface IPhoneValidationService
{
    /// <summary>
    /// Validates <paramref name="phoneNumber"/> against <paramref name="countryCode"/>.
    /// Use "ZZ" to require a leading '+' (E.164 enforcement at the boundary).
    /// Never throws — returns IsValid=false on any parse error.
    /// </summary>
    PhoneValidationResult Validate(string phoneNumber, string countryCode);

    /// <summary>
    /// Returns the E.164 representation, or null when the number is invalid.
    /// </summary>
    string? ToE164(string? phoneNumber, string countryCode);

    /// <summary>
    /// Returns true when the number is a mobile number for the given country.
    /// </summary>
    bool IsMobile(string phoneNumber, string countryCode);
}
