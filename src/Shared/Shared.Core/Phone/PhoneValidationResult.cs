namespace Shared.Core.Phone;

public record PhoneValidationResult(
    bool IsValid,
    string? E164,
    string? International,
    string? National,
    PhoneNumberKind Kind,
    string CountryCode);
