using PhoneNumbers;

namespace Shared.Core.Phone;

/// <summary>
/// Thread-safe singleton. PhoneNumberUtil is thread-safe by design.
/// To add country-specific overrides wrap this in a decorator and register it via Scrutor:
///   services.Decorate&lt;IPhoneValidationService, MyCountryPhoneService&gt;()
/// Never modify this class for per-country logic.
/// </summary>
public sealed class PhoneValidationService : IPhoneValidationService
{
    private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();

    public PhoneValidationResult Validate(string phoneNumber, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return Invalid(countryCode);

        try
        {
            var region = countryCode.ToUpperInvariant();
            var parsed = PhoneUtil.Parse(phoneNumber, region);

            if (!PhoneUtil.IsValidNumber(parsed))
                return Invalid(countryCode);

            return new PhoneValidationResult(
                IsValid: true,
                E164: PhoneUtil.Format(parsed, PhoneNumberFormat.E164),
                International: PhoneUtil.Format(parsed, PhoneNumberFormat.INTERNATIONAL),
                National: PhoneUtil.Format(parsed, PhoneNumberFormat.NATIONAL),
                Kind: MapKind(PhoneUtil.GetNumberType(parsed)),
                CountryCode: countryCode.ToUpperInvariant());
        }
        catch (NumberParseException)
        {
            return Invalid(countryCode);
        }
    }

    public string? ToE164(string? phoneNumber, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return null;

        var result = Validate(phoneNumber, countryCode);
        return result.IsValid ? result.E164 : null;
    }

    public bool IsMobile(string phoneNumber, string countryCode)
        => Validate(phoneNumber, countryCode).Kind == PhoneNumberKind.Mobile;

    private static PhoneValidationResult Invalid(string countryCode) =>
        new(false, null, null, null, PhoneNumberKind.Unknown, countryCode.ToUpperInvariant());

    private static PhoneNumberKind MapKind(PhoneNumberType type) => type switch
    {
        PhoneNumberType.MOBILE => PhoneNumberKind.Mobile,
        PhoneNumberType.FIXED_LINE => PhoneNumberKind.FixedLine,
        PhoneNumberType.FIXED_LINE_OR_MOBILE => PhoneNumberKind.Mobile,
        PhoneNumberType.TOLL_FREE => PhoneNumberKind.TollFree,
        PhoneNumberType.PREMIUM_RATE => PhoneNumberKind.Premium,
        _ => PhoneNumberKind.Unknown
    };
}
